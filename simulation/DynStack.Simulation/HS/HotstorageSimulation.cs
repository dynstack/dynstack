using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DynStack.DataModel;
using DynStack.DataModel.HS;
using SimSharp;

namespace DynStack.Simulation.HS {
  public enum MoveCondition {
    Valid,
    InvalidBlockId,
    InvalidSource,
    InvalidTarget,
    HeightLimitViolated,
    BlockNotFound,
    HandoverNotReady,
    BlockNotReady
  }
  public class HotstorageSimulation {
    private PseudoRealtimeSimulation sim;
    public const int MAX_OBSERVATIONS = 100;
    public World World { get; private set; }
    private int blockIds;
    private Resource crane, handover, upstream;
    private int lastScheduleSequence;
    private Settings settings { get; }
    private PcgRandom craneRNG;
    private PcgRandom productionRNG;
    private PcgRandom handoverRNG;
    private PcgRandom readyRNG;

    public IPolicy policy;
    public SampleMonitor ServiceLevel { get; private set; }
    public SampleMonitor LeadTimes { get; private set; }
    public SampleMonitor Tardiness { get; private set; }
    public TimeSeriesMonitor BufferUtilization { get; private set; }
    public TimeSeriesMonitor CraneUtilization { get; private set; }
    public TimeSeriesMonitor HandoverUtilization { get; private set; }
    public TimeSeriesMonitor UpstreamUtilization { get; private set; }
    public bool SimulateAsync { get; set; }
    public void SetLogger(TextWriter logger) {
      sim.Logger = logger;
    }

    public TimeStamp Now => ToTimeStamp(sim.Now);

    public async Task<World> RunAsync() {
      sim.Process(OrderGenerator());
      if (World.Handover.Block != null)
        sim.Process(OrderCompletion());

      OnWorldChanged();
      await sim.RunAsync(settings.SimulationDuration);

      World.KPIs.CraneUtilizationMean = CraneUtilization.Mean;
      World.KPIs.HandoverUtilizationMean = HandoverUtilization.Mean;
      World.KPIs.UpstreamUtilizationMean = UpstreamUtilization.Mean;
      World.KPIs.BlockedArrivalTime = (1 - UpstreamUtilization.Mean) * (sim.Now - sim.StartDate).TotalSeconds;

      var remainingBlocks = World.BlocksInSystem();
      foreach (var block in remainingBlocks) {
        if (block.Due < Now) {
          Tardiness.Add((Now - block.Due).TotalSeconds);
          World.KPIs.TardinessMean = Tardiness.Mean;
        }
      }

      OnWorldChanged(kpichange: true);

      return World;
    }

    public World Run() {
      sim.Process(OrderGenerator());
      if (World.Handover.Block != null)
        sim.Process(OrderCompletion());

      OnWorldChanged();
      sim.Run(settings.SimulationDuration);
      World.KPIs.CraneUtilizationMean = CraneUtilization.Mean;
      World.KPIs.HandoverUtilizationMean = HandoverUtilization.Mean;
      World.KPIs.UpstreamUtilizationMean = UpstreamUtilization.Mean;
      World.KPIs.BlockedArrivalTime = (1 - UpstreamUtilization.Mean) * (sim.Now - sim.StartDate).TotalSeconds;

      var remainingBlocks = World.BlocksInSystem();
      foreach (var block in remainingBlocks) {
        Tardiness.Add((Now - block.Due).TotalSeconds);
        World.KPIs.TardinessMean = Tardiness.Mean;
      }

      OnWorldChanged(kpichange: true);
      return World;
    }

    public void StopAsync() {
      sim.StopAsync();
    }

    private double CalculateGirderPosition(int locationId) {
      return (double)locationId / (settings.BufferCount + 1);
    }

    private double CalculateHoistPosition(int level) {
      return (double)level / (settings.BufferMaxHeight + 1);
    }

    public HotstorageSimulation(Settings set) {
      settings = set;
      sim = new PseudoRealtimeSimulation(DateTime.UtcNow, settings.Seed);
      ServiceLevel = new SampleMonitor("SerivceLevel");
      LeadTimes = new SampleMonitor("LeadTimes");
      Tardiness = new SampleMonitor("Tardiness");
      BufferUtilization = new TimeSeriesMonitor(sim, "BufferUtilization");
      CraneUtilization = new TimeSeriesMonitor(sim, "CraneUtilization");
      HandoverUtilization = new TimeSeriesMonitor(sim, "HandoverUtilization");
      UpstreamUtilization = new TimeSeriesMonitor(sim, "UpstreamUtilization");
      lastScheduleSequence = int.MinValue;
      World = new World {
        Production = new Stack { Id = 0, MaxHeight = settings.ProductionMaxHeight, BottomToTop = new List<Block>() },
        Buffers = Enumerable.Range(1, settings.BufferCount).Select(x => new Stack { Id = x, MaxHeight = settings.BufferMaxHeight, BottomToTop = new List<Block>() }).ToList(),
        Handover = new Handover() { Id = settings.BufferCount + 1, Ready = false },
        Now = new TimeStamp(),
        Crane = new Crane { Id = 0, LocationId = 0, Schedule = new CraneSchedule() { Moves = new List<CraneMove>() }, Load = null, GirderPosition = 0.0, HoistPosition = 1.0 },
        KPIs = new Performance(),
        ObservationData = new Uncertainties(),
        InvalidMoves = new List<CraneMove>()
      };
      crane = new Resource(sim, capacity: 1) { Utilization = CraneUtilization };
      handover = new Resource(sim, capacity: 1) { Utilization = HandoverUtilization };
      upstream = new Resource(sim, capacity: 1) { Utilization = UpstreamUtilization };
      InitializeWorldState();
    }
    /// <summary>
    /// In this mode the simulation uses an integrated policy that is called directly via C# method invocation
    /// and does not need to interact asynchronously via network. This is not a realistic case, as the simulation
    /// will pause and await the policy's result, and thus also runs in virtual time.
    /// </summary>
    /// <param name="set">The settings that the simulation should run with.</param>
    /// <param name="policy">The policy that handles the crane orders.</param>
    public HotstorageSimulation(Settings set, IPolicy policy) : this(set) {
      this.policy = policy;
      sim.SetVirtualtime();
    }

    private void InitializeWorldState() {
      var rand = new Random(settings.Seed);
      var initRand = new PcgRandom(rand.Next());
      craneRNG = new PcgRandom(rand.Next());
      productionRNG = new PcgRandom(rand.Next());
      handoverRNG = new PcgRandom(rand.Next());
      readyRNG = new PcgRandom(rand.Next());
      var capacity = World.Buffers.Sum(x => x.MaxHeight);
      var past = sim.Now;
      for (var i = 0; i < settings.InitialNumberOfBlocks; i++) {
        past -= sim.RandLogNormal2(initRand, settings.ArrivalTimeMean, settings.ArrivalTimeStd);
        var b = new Block() {
          Id = ++blockIds,
          Release = ToTimeStamp(past),
          Ready = false,
          Due = ToTimeStamp(past + sim.RandLogNormal2(initRand, settings.DueTimeMean, settings.DueTimeStd))
        };
        while (b.Due < ToTimeStamp(sim.Now + settings.DueTimeMin)) {
          b.Due += sim.RandLogNormal2(initRand, settings.DueTimeMean, settings.DueTimeStd);
        }
        var readyFactor = settings.ReadyFactorMin + initRand.NextDouble() * (settings.ReadyFactorMax - settings.ReadyFactorMin);
        b.Ready = (b.Release + TimeSpan.FromSeconds(readyFactor * (b.Due - b.Release).TotalSeconds)) < Now;

        var possibleBuffers = World.Buffers.Where(x => x.BottomToTop.Count < x.MaxHeight).ToList();
        var buf = possibleBuffers[initRand.Next(possibleBuffers.Count)];
        if (buf.BottomToTop.Count == 0) buf.BottomToTop.Add(b);
        else buf.BottomToTop.Insert(0, b);

        sim.Process(BlockProcess(b, suppressEvent: true));
      }
      BufferUtilization.UpdateTo(World.Buffers.Sum(x => x.Height) / (double)World.Buffers.Sum(x => x.MaxHeight));
      World.KPIs.BufferUtilizationMean = BufferUtilization.Mean;
      World.Production.BottomToTop.Add(new Block() {
        Id = ++blockIds,
        Release = ToTimeStamp(sim.Now),
        Ready = false,
        Due = ToTimeStamp(sim.Now + sim.RandLogNormal2(initRand, settings.DueTimeMean, settings.DueTimeStd))
      });
      World.Handover.Ready = true;
      sim.Process(BlockProcess(World.Production.BottomToTop.Single(), suppressEvent: true));
      sim.Process(WorldUpdates());
    }

    public async Task SetCraneScheduleAsync(CraneSchedule schedule) {
      await Task.Run(() => {
        sim.Process(Crane(schedule));
      });
    }

    private IEnumerable<Event> OrderGenerator() {
      using (var req = upstream.Request()) {
        yield return req;

        while (true) {
          var before = Now;
          //yield return sim.TimeoutExponential(productionRNG, settings.ArrivalInterval);
          yield return sim.Timeout(sim.RandLogNormal2(productionRNG, settings.ArrivalTimeMean, settings.ArrivalTimeStd));

          if (World.Production.BottomToTop.Count >= World.Production.MaxHeight) {
            // uh-oh production stack is full, very bad!
            //sim.Log("{0} Production stack full, upstream process halts!", Now);
            break;
          }
          var block = new Block {
            Id = ++blockIds,
            Release = ToTimeStamp(sim.Now),
            Ready = false,
            //Due = ToTimeStamp(sim.Now + settings.DueTimeMin + sim.RandExponential(productionRNG, settings.DueTimeMean))
            Due = ToTimeStamp(sim.Now + sim.RandLogNormal2(productionRNG, settings.DueTimeMean, settings.DueTimeStd))
          };
          World.Production.BottomToTop.Insert(0, block);
          sim.Process(BlockProcess(block));
          var interval = Now - before;
          World.ObservationData.ArrivalIntervals.AddLast(interval.TotalSeconds);
          if (World.ObservationData.ArrivalIntervals.Count > MAX_OBSERVATIONS)
            World.ObservationData.ArrivalIntervals.RemoveFirst();
          OnWorldChanged();
          //sim.Log("{0} New block ({1}) released, due: {2}", Now, block.Id, block.Due);
        }
      }
    }

    private IEnumerable<Event> BlockProcess(Block block, bool suppressEvent = false) {
      World.KPIs.TotalBlocksOnTime++;
      if (!suppressEvent) OnWorldChanged(kpichange: true);
      if (!block.Ready) {
        var delta = (Now - block.Release); // if block release was already, e.g. after initializing world state
        var timeout = TimeSpan.FromSeconds(sim.RandUniform(readyRNG, settings.ReadyFactorMin, settings.ReadyFactorMax) * (block.Due - block.Release).TotalSeconds) - delta;
        if (timeout > TimeSpan.Zero)
          yield return sim.Timeout(timeout);
        block.Ready = true;
        if (!suppressEvent || timeout > TimeSpan.Zero) OnWorldChanged();
        //sim.Log("{0} Block ({1}) is ready, due: {2}", Now, block.Id, block.Due);
      }
      var untilDue = block.Due - Now + settings.CheckInterval;
      if (untilDue > TimeSpan.Zero) yield return sim.Timeout(untilDue);
      if (block.Delivered) yield break;
      ServiceLevel.Add(0);
      World.KPIs.ServiceLevelMean = ServiceLevel.Mean;
      World.KPIs.TotalBlocksOnTime--;
      OnWorldChanged(kpichange: true);
      //sim.Log($"{Now} Block ({block.Id}) is overdue");
    }

    // on demand calculation of crane position for world state
    private TimeStamp moveStart;
    private double fromPos;
    private double toPos;
    private MoveDirection moveDirection;
    private TimeSpan moveDuration;

    private enum MoveDirection { Horizontal, Vertical }

    public World GetEstimatedWorldState() {
      if (World == null) return World;

      World.Now = Now;

      var pos = fromPos;
      var moveLength = toPos - pos;
      if (moveLength == 0) return World;

      var elapsed = Now - moveStart;
      if (moveDuration.TotalMilliseconds > 0)
        pos += moveLength * elapsed.TotalMilliseconds / moveDuration.TotalMilliseconds;

      switch (moveDirection) {
        case MoveDirection.Horizontal: World.Crane.GirderPosition = pos; break;
        case MoveDirection.Vertical: World.Crane.HoistPosition = pos; break;
      }

      return World;
    }

    private IEnumerable<Event> Crane(CraneSchedule schedule, double delay = 0) {
      if (delay > 0) yield return sim.Timeout(TimeSpan.FromSeconds(delay));

      if (schedule.SequenceNr < 0) {
        OnWorldChanged();
        yield break;
      }
      if (schedule.Moves == null) schedule.Moves = new List<CraneMove>(1);

      if (schedule.SequenceNr < lastScheduleSequence) yield break;
      lastScheduleSequence = schedule.SequenceNr;

      using (var req = crane.Request()) {
        yield return req;
        if (schedule.SequenceNr < lastScheduleSequence) yield break;
        if (schedule.Moves.Count == 0) {
          if (World.Crane.Schedule.Moves.Count > 0 || World.Crane.Schedule.SequenceNr < schedule.SequenceNr) {
            World.Crane.Schedule = schedule;
            OnWorldChanged();
          }
          yield break;
        }

        World.Crane.Schedule = schedule;
        schedule.Moves = schedule.Moves.OrderBy(x => x.Sequence).ToList();
        OnWorldChanged();
        //sim.Log("{0} Crane working on {1} order(s) in schedule #{2}", Now, schedule.Moves?.Count, schedule.SequenceNr);

        var removes = false;
        if (schedule.Moves == null) { yield break; }
        foreach (var move in schedule.Moves.ToList()) {
          var condition = CheckMoveCondition(move);
          if (condition != MoveCondition.Valid) {
            //sim.Log($"Invalid Move {move} {condition}");
            World.Crane.Schedule.Moves.Remove(move);
            World.InvalidMoves.Add(move);
            removes = true;
            continue;
          }
          if (removes) {
            //sim.Log("{0} Crane had to skip some infeasible orders.", Now);
            OnWorldChanged();
            removes = false;
          }

          if (move.EmptyMove) {
            //sim.Log("{0} Next move (seq: {2}): Position crane over stack {1}.", Now, move.TargetId, move.Sequence);
            yield return sim.Process(MoveCraneHorizontal(move.TargetId));
            World.Crane.Schedule.Moves.Remove(move);
            OnWorldChanged();
            if (schedule.SequenceNr < lastScheduleSequence) yield break;
            continue;
          }

          if (move.SourceId == World.Production.Id) {
            if (move.TargetId == World.Handover.Id) {
              //sim.Log("{0} Next move (seq: {2}): Directly remove block {1}.", Now, move.BlockId, move.Sequence);
            } else {
              //sim.Log("{0} Next move (seq: {3}): Put block {1} at buffer {2}.", Now, move.BlockId, move.TargetId, move.Sequence);
            }
          } else if (move.TargetId == World.Handover.Id) {
            //sim.Log("{0} Next move (seq: {3}): Remove block {1} from {2}.", Now, move.BlockId, move.SourceId, move.Sequence);
          } else {
            //sim.Log("{0} Next move (seq: {4}): Relocate block {1} from {2} to {3}.", Now, move.BlockId, move.SourceId, move.TargetId, move.Sequence);
          }


          // move crane to source position (or target if just a crane move without relocation)
          if (World.Crane.LocationId != move.SourceId) {
            yield return sim.Process(MoveCraneHorizontal(move.SourceId));
          }

          // we provide crane relocation time only with respect to the material flow
          var before = Now;

          var sourceBuffer = move.SourceId == World.Production.Id ? World.Production : World.Buffers[move.SourceId - 1];
          // move crane down for pickup
          yield return sim.Process(MoveCraneVertical(HoistAction.Pickup, sourceBuffer.BottomToTop.Count - 1, sourceBuffer));

          // restart upstream activities
          if (move.SourceId == World.Production.Id && upstream.InUse == 0)
            sim.Process(OrderGenerator());

          // move crane up from pickup
          yield return sim.Process(MoveCraneVertical(action: HoistAction.Move, level: settings.BufferMaxHeight + 1));

          // move crane to target position
          yield return sim.Process(MoveCraneHorizontal(move.TargetId));

          // move crane down for dropoff
          var stack = move.TargetId <= World.Buffers.Count
            ? (IStackingLocation)World.Buffers[move.TargetId - 1] : World.Handover;
          yield return sim.Process(MoveCraneVertical(HoistAction.Dropoff, stack.Height, stack));

          // trigger removal of block
          if (move.TargetId == World.Handover.Id)
            sim.Process(OrderCompletion());

          // move crane up from dropoff
          yield return sim.Process(MoveCraneVertical(action: HoistAction.Move, level: settings.BufferMaxHeight + 1));

          World.Crane.Schedule.Moves.Remove(move);

          var elapsed = Now - before;
          World.ObservationData.CraneMoveTimes.AddLast(elapsed.TotalSeconds);
          if (World.ObservationData.CraneMoveTimes.Count > MAX_OBSERVATIONS)
            World.ObservationData.CraneMoveTimes.RemoveFirst();

          OnWorldChanged();
          if (schedule.SequenceNr < lastScheduleSequence) yield break;
        }
        if (removes) {
          //sim.Log("{0} Crane had to skip some infeasible orders.", Now);
          OnWorldChanged();
        }
      }
    }

    private IEnumerable<Event> MoveCraneHorizontal(int targetLocationId) {
      moveStart = Now;
      fromPos = CalculateGirderPosition(World.Crane.LocationId);
      toPos = CalculateGirderPosition(targetLocationId);
      moveDirection = MoveDirection.Horizontal;
      var low = settings.CraneMoveTimeMean.TotalSeconds - settings.CraneMoveTimeStd.TotalSeconds;
      var high = settings.CraneMoveTimeMean.TotalSeconds + settings.CraneMoveTimeStd.TotalSeconds;
      moveDuration = TimeSpan.FromSeconds(sim.RandTriangular(craneRNG, low, high) * Math.Abs(toPos - fromPos));

      yield return sim.Timeout(moveDuration);

      World.Crane.GirderPosition = toPos;
      World.Crane.LocationId = targetLocationId;
      fromPos = toPos;
      //sim.Log($"{Now} Crane moved to location {World.Crane.LocationId}");
      OnWorldChanged();
    }

    private enum HoistAction { Move, Pickup, Dropoff }

    private IEnumerable<Event> MoveCraneVertical(HoistAction action, int level, IStackingLocation stack = null) {
      moveStart = Now;
      fromPos = World.Crane.HoistPosition;
      toPos = CalculateHoistPosition(level);
      moveDirection = MoveDirection.Vertical;
      var low = settings.HoistMoveTimeMean.TotalSeconds - settings.HoistMoveTimeStd.TotalSeconds;
      var high = settings.HoistMoveTimeMean.TotalSeconds + settings.HoistMoveTimeStd.TotalSeconds;
      moveDuration = TimeSpan.FromSeconds(sim.RandTriangular(craneRNG, low, high) * Math.Abs(toPos - fromPos));

      yield return sim.Timeout(moveDuration);

      if (action == HoistAction.Pickup) {
        World.Crane.Load = stack.Pickup();
        //sim.Log($"{Now} Crane picked up block ({World.Crane.Load.Id}) at location {World.Crane.LocationId}");
        World.KPIs.CraneManipulations++;
      } else if (action == HoistAction.Dropoff) {
        var block = World.Crane.Load;
        stack.Drop(block);
        World.Crane.Load = null;
        //sim.Log($"{Now} Crane dropped off block ({block.Id}) at location {World.Crane.LocationId}");
      }
      BufferUtilization.UpdateTo(World.Buffers.Sum(x => x.Height) / (double)World.Buffers.Sum(x => x.MaxHeight));
      World.KPIs.BufferUtilizationMean = BufferUtilization.Mean;

      World.Crane.HoistPosition = toPos;
      fromPos = toPos;

      OnWorldChanged();
    }

    private MoveCondition CheckMoveCondition(CraneMove move) {
      if (move.EmptyMove) return MoveCondition.Valid; // just a crane relocation
      if (move.SourceId == 0) { // Production -> X
        var block = World.Production.BottomToTop.LastOrDefault();
        if (block == null || block.Id != move.BlockId) { return MoveCondition.BlockNotFound; }
      } else if (move.SourceId <= World.Buffers.Count) { // Buffer -> X
        var block = World.Buffers[move.SourceId - 1].BottomToTop.LastOrDefault();
        if (block == null || block.Id != move.BlockId) return MoveCondition.BlockNotFound;
      } else return MoveCondition.InvalidSource; // Handover -> X
      if (move.TargetId == 0) return MoveCondition.InvalidTarget; // X -> Production
      else if (move.TargetId <= World.Buffers.Count) { // X -> Buffer
        if (World.Buffers[move.TargetId - 1].BottomToTop.Count >= World.Buffers[move.TargetId - 1].MaxHeight)
          return MoveCondition.HeightLimitViolated;
      } else { // X -> Handover
        if (!World.Handover.Ready || World.Handover.Block != null) return MoveCondition.HandoverNotReady;
        Block block;
        if (move.SourceId == World.Production.Id) block = World.Production.BottomToTop.LastOrDefault();
        else block = World.Buffers[move.SourceId - 1].BottomToTop.LastOrDefault();
        if (!block.Ready) return MoveCondition.BlockNotReady;
      }
      return MoveCondition.Valid;
    }

    private IEnumerable<Event> OrderCompletion() {
      var before = Now;
      using (var req = handover.Request()) {
        yield return req;

        var block = World.Handover.Block;
        while (!block.Ready) { yield return sim.Timeout(settings.CheckInterval); } // TODO: when ready ?!
        block.Delivered = true;

        if (block.Due >= Now) {
          ServiceLevel.Add(1); // A 0 will be entered by BlockProcess
          World.KPIs.ServiceLevelMean = ServiceLevel.Mean;
          Tardiness.Add(0);
          World.KPIs.TardinessMean = Tardiness.Mean;
        } else {
          var tardiness = Now - block.Due;
          Tardiness.Add(tardiness.TotalSeconds);
          World.KPIs.TardinessMean = Tardiness.Mean;
        }
        var leadTime = Now - block.Release;
        LeadTimes.Add(leadTime.TotalSeconds);
        World.KPIs.LeadTimeMean = LeadTimes.Mean;
        World.KPIs.DeliveredBlocks++;
        OnWorldChanged(kpichange: true);
        yield return sim.TimeoutTriangular(handoverRNG, settings.MinClearTime, settings.MaxClearTime);
        World.Handover.Block = null;
        World.Handover.Ready = false;
        OnWorldChanged();
        //sim.Log("{0} Block ({1}) is removed, due: {2}", Now, block.Id, block.Due);
        //sim.Log("{0} KPIs! Manipulations = {1}, SL = {2:F1}, Delivered = {3}, Leadtime = {4:F2}", Now, World.KPIs.CraneManipulations, World.KPIs.ServiceLevelMean, World.KPIs.DeliveredBlocks, World.KPIs.LeadTimeMean);

        //yield return sim.TimeoutExponential(handoverRNG, settings.HandoverInterval);
        yield return sim.Timeout(sim.RandLogNormal2(handoverRNG, settings.HandoverTimeMean, settings.HandoverTimeStd));
        World.Handover.Ready = true;
        var elapsed = Now - before;
        World.ObservationData.HandoverReadyIntervals.AddLast(elapsed.TotalSeconds);
        if (World.ObservationData.HandoverReadyIntervals.Count > MAX_OBSERVATIONS)
          World.ObservationData.HandoverReadyIntervals.RemoveFirst();
        OnWorldChanged();
        //sim.Log("{0} Handover is ready", Now);
      }
    }

    private bool _worldChanged;
    public event EventHandler WorldChanged;
    private void OnWorldChanged(bool kpichange = false) {
      _worldChanged = true;
    }
    private IEnumerable<Event> WorldUpdates() {
      var updateInterval = 1000L; // must be > 1
      while (true) {
        // uncomment if you prefer world updates only for changes
        //if (_worldChanged) {

        if (World.PolicyTime > 0) {
          // Simulate that the policy took a certain to calculate
          World.PolicyTime = Math.Max(World.PolicyTime - updateInterval, 0L); // milliseconds
        }

        World.Now = Now;
        World.KPIs.CraneUtilizationMean = CraneUtilization.Mean;
        World.KPIs.HandoverUtilizationMean = HandoverUtilization.Mean;
        World.KPIs.UpstreamUtilizationMean = UpstreamUtilization.Mean;
        World.KPIs.BlockedArrivalTime = (1 - UpstreamUtilization.Mean) * (sim.Now - sim.StartDate).TotalSeconds;

        if (World.PolicyTime == 0) {
          var sw = Stopwatch.StartNew();
          var schedule = policy?.GetSchedule(World);
          sw.Stop();
          World.PolicyTime = SimulateAsync ? sw.ElapsedMilliseconds : 1L;

          if (schedule != null)
            sim.Process(Crane(schedule, World.PolicyTime));

        }

        WorldChanged?.Invoke(this, EventArgs.Empty);
        World.InvalidMoves.Clear();
        _worldChanged = false;
        //}
        yield return sim.Timeout(TimeSpan.FromMilliseconds(updateInterval));
      }
    }

    private TimeStamp ToTimeStamp(DateTime dt) {
      var ms = Math.Round((dt - sim.StartDate).TotalMilliseconds);
      return new TimeStamp((long)ms);
    }
  }
}
