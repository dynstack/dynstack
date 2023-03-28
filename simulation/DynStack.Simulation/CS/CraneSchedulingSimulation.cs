using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DynStack.DataModel;
using DynStack.DataModel.CS;
using SimSharp;
using Simulation.Util;
using MCP = DynStack.Simulation.CS.Policy.MoveCreation;
using TLRP = DynStack.Simulation.CS.Policy.TargetLocationRecommendation;

namespace DynStack.Simulation.CS {
  public class CraneSchedulingSimulation : IStackingEnvironment {
    public World World { get; private set; }
    private Settings settings { get; }

    private MoveGenerator _moveGenerator;

    private int blockId;
    private int moveRequestId;

    private PseudoRealtimeSimulation sim;
    public SimSharp.Simulation Environment => sim;
    public TimeStamp Now => sim.NowTS();
    public int Height => World.Height;
    public double Width => World.Width;

    private Dictionary<ILocationResource, Location> _locationLookup;

    private List<ILocationResource> _locations;
    private Dictionary<int, ILocationResource> _locationResourceLookup;
    private List<ICraneAgent> _agents;
    private List<ICraneMoveEvent> _moves;
    private ICraneScheduleStore _scheduleStore;
    private IZoneControl _zoneControl;

    private Store _upstreamStore;
    private HashSet<ILocationResource> _freeArrivals;
    private Store _downstreamStore;
    private HashSet<ILocationResource> _freeHandovers;
    private HashSet<int> _orderedBlockIds;
    private Dictionary<ILocationResource, TimeStamp> _parkingTimes;

    private int _servicedUpstreamVehicles;
    private TimeSpan _upstreamServiceTime;
    private int _servicedDownstreamVehicles;
    private TimeSpan _downstreamServiceTime;

    public IEnumerable<ILocationResource> LocationResources => _locations;
    public IEnumerable<ICraneAgent> CraneAgents => _agents;
    public IEnumerable<ICraneMoveEvent> CraneMoves => _moves;
    public IEnumerable<IMoveRequest> MoveRequests => World.MoveRequests;
    public IEnumerable<IBlock> Blocks => World.AllBlocks();

    public ICraneScheduleStore CraneScheduleStore => _scheduleStore;
    public IZoneControl ZoneControl => _zoneControl;

    public CraneSchedulingSimulation(Settings set) {
      settings = set;
      sim = new PseudoRealtimeSimulation(DateTime.UtcNow, settings.Seed);

      World = new World {
        Now = new TimeStamp(0),
        Width = set.Width,
        Height = set.Height,
        Locations = new List<Location>(),
        CraneMoves = new List<CraneMove>(),
        Cranes = new List<Crane>(),
        CraneSchedule = new CraneSchedule(),
        MoveRequests = new List<MoveRequest>(),
        KPIs = new Performance()
      };

      #region Arrival Locations
      var stackId = 0;
      foreach (var p in set.ArrivalStackPositions)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.ArrivalStack,
          GirderPosition = p,
          MaxHeight = set.MaxHeightForArrival,
          Stack = new Stack()
        });
      #endregion
      #region Buffer Locations
      var zipped = set.BufferStackPositions
                      .Zip(set.BufferStackClasses, (p, c) => (p, c));
      foreach (var (p, c) in zipped)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.Buffer,
          GirderPosition = p,
          MaxHeight = set.MaxHeightForBuffer,
          Stack = new Stack(),
          Class = c
        });
      #endregion
      #region Handover Locations
      foreach (var p in set.HandoverStackPositions)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.HandoverStack,
          GirderPosition = p,
          MaxHeight = set.MaxHeightForHandover,
          Stack = new Stack(),
        });
      #endregion

      var minPos = 0;
      var maxPos = set.Width;
      var sd = set.SafetyDistance;

      #region Cranes
      World.Cranes.Add(new Crane {
        Id = 1,
        CraneCapacity = 1,
        Load = new Stack(),
        HoistLevel = settings.Height,
        GirderPosition = minPos,
        MinPosition = minPos,
        MaxPosition = maxPos - sd,
        Width = sd,
      });

      World.Cranes.Add(new Crane {
        Id = 2,
        CraneCapacity = 1,
        Load = new Stack(),
        HoistLevel = settings.Height,
        GirderPosition = maxPos,
        MinPosition = minPos + sd,
        MaxPosition = maxPos,
        Width = sd,
      });
      #endregion
    }

    public void SetLogger(TextWriter logger) {
      Environment.Logger = logger;
    }

    private void Initialize() {
      _locations = new List<ILocationResource>();
      _locationResourceLookup = new Dictionary<int, ILocationResource>();
      _locationLookup = new Dictionary<ILocationResource, Location>();
      _agents = new List<ICraneAgent>();
      _moves = new List<ICraneMoveEvent>();
      _freeArrivals = new HashSet<ILocationResource>();
      _freeHandovers = new HashSet<ILocationResource>();
      _orderedBlockIds = new HashSet<int>();
      _parkingTimes = new Dictionary<ILocationResource, TimeStamp>();

      var seedRng = new PcgRandom(settings.Seed);

      #region Locations
      foreach (var loc in World.Locations) {
        loc.Stack.BottomToTop.Clear();

        var locRes = new LocationResource(this, loc);
        _locations.Add(locRes);
        _locationResourceLookup.Add(loc.Id, locRes);
        _locationLookup.Add(locRes, loc);

        switch (loc.Type) {
          case StackTypes.ArrivalStack: _freeArrivals.Add(locRes); break;
          case StackTypes.HandoverStack: _freeHandovers.Add(locRes); break;
        }
      }
      #endregion

      #region Initial Blocks
      var util = settings.InitialBufferUtilization;
      var utilRng = new PcgRandom(seedRng.Next());
      foreach (var loc in World.BufferLocations) {
        for (int h = 0; h < loc.MaxHeight; h++) {
          if (utilRng.NextDouble() < util) {
            loc.Stack.BottomToTop.Add(new Block { Id = blockId++ });
          }
        }
      }
      #endregion

      #region Cranes
      var meanGSpeedShuffle = settings.Width / settings.CraneMoveTimeMean.TotalSeconds;
      var cvGSpeedShuffle = settings.CraneMoveTimeStd.TotalSeconds / settings.CraneMoveTimeMean.TotalSeconds;
      var meanHSpeedShuffle = settings.Height / settings.HoistMoveTimeMean.TotalSeconds;
      var cvHSpeedShuffle = settings.HoistMoveTimeStd.TotalSeconds / settings.HoistMoveTimeMean.TotalSeconds;

      foreach (var crane in World.Cranes) {
        _agents.Add(
          new CSCraneAgent(this, crane,
            girderSpeed: new LognormalDistribution(sim, meanGSpeedShuffle, meanGSpeedShuffle * cvGSpeedShuffle),
            hoistSpeed: new LognormalDistribution(sim, meanHSpeedShuffle, meanHSpeedShuffle * cvHSpeedShuffle),
            manipulationTime: new LognormalDistribution(sim, settings.CraneManipulationTimeMean.TotalSeconds, settings.CraneManipulationTimeStd.TotalSeconds)
          )
        );
      }
      #endregion

      _scheduleStore = new CSCraneScheduleStore(this, World.CraneSchedule);
      _zoneControl = new ZoneControl(this);
      _moveGenerator = new MoveGenerator(seedRng.Next(), new MCP.DefaultPolicy(seedRng.Next()), new TLRP.RandomPolicy(seedRng.Next()), _moves, _agents, World, this);
      _upstreamStore = new Store(Environment);
      _downstreamStore = new Store(Environment);

      #region Initial Vehicles
      var initVehicleRng = new PcgRandom(seedRng.Next());

      var initialUpstreamVehicleCount = 1 + initVehicleRng.Next(_freeArrivals.Count);
      for (int i = 0; i < initialUpstreamVehicleCount; i++) {
        GenerateUpstreamVehicle(initVehicleRng.Next(), _upstreamStore);
      }

      var initialDownstreamVehicleCount = 1 + initVehicleRng.Next(_freeHandovers.Count);
      for (int i = 0; i < initialDownstreamVehicleCount; i++) {
        GenerateDownstreamVehicle(initVehicleRng.Next(), _downstreamStore);
      }
      #endregion

      Environment.Process(UpstreamVehicleDispatcher(seedRng.Next(), _upstreamStore));
      Environment.Process(UpstreamVehicleGenerator(seedRng.Next(), _upstreamStore));

      Environment.Process(DownstreamVehicleDispatcher(seedRng.Next(), _downstreamStore));
      Environment.Process(DownstreamVehicleGenerator(seedRng.Next(), _downstreamStore));

      foreach (var agent in _agents) {
        sim.Process(CountManipulations(agent));
      }

      Environment.Process(WorldUpdates());
      //Environment.Process(SpaceTimeDrawing());
    }

    public Timeout ReactionTime() {
      return Environment.Timeout(TimeSpan.FromMilliseconds(200));
    }
    public Timeout AtLeastReactionTime(TimeSpan timeout) {
      if (timeout < TimeSpan.FromMilliseconds(200)) return ReactionTime();
      return Environment.Timeout(timeout);
    }
    public Event AtLeastReactionTime(IEnumerable<Event> generator, int priority = 0) {
      return Environment.Process(generator, priority) & ReactionTime();
    }

    public async Task<World> RunAsync() {
      Initialize();

      await sim.RunAsync(settings.SimulationDuration);

      return World;
    }

    public void StopAsync() => Environment.StopAsync();

    public async Task SetSolutionAsync(CraneSchedulingSolution sol) {
      await Task.Run(() => Environment.Process(SetPlannedMovesWithDelay(TimeSpan.FromMilliseconds(200), sol)));
    }

    private IEnumerable<Event> SetPlannedMovesWithDelay(TimeSpan delay, CraneSchedulingSolution sol) {
      yield return sim.Timeout(delay);

      if (!ValidateSolution(sol)) {
        Environment.Log($"{Now}: invalid crane scheduling solution received");
        yield break;
      }

      var locIdToLocPos = World.Locations.ToDictionary(x => x.Id, x => x.GirderPosition);
      var active = World.CraneSchedule.Activities.Where(x => x.State == CraneScheduleActivityState.Active).ToArray();
      var activeIds = new HashSet<int>(active.Select(x => x.MoveId));

      foreach (var m in sol.CustomMoves) {
        if (m.Type == MoveType.MoveToPickup)
          m.DropoffLocationId = m.PickupLocationId;

        if (locIdToLocPos.TryGetValue(m.PickupLocationId, out var pickPos))
          m.PickupGirderPosition = pickPos;
        else continue; // ignore the move

        if (locIdToLocPos.TryGetValue(m.DropoffLocationId, out var dropPos))
          m.DropoffGirderPosition = dropPos;
        else continue; // ignore the move

        if (activeIds.Contains(m.Id)) continue; // ignore the move

        var existingMove = World.CraneMoves.SingleOrDefault(x => x.Id == m.Id);
        if (existingMove != null) {
          // update custom move
          existingMove.Type = m.Type;
          existingMove.PickupLocationId = m.PickupLocationId;
          existingMove.PickupGirderPosition = m.PickupGirderPosition;
          existingMove.DropoffLocationId = m.DropoffLocationId;
          existingMove.DropoffGirderPosition = m.DropoffGirderPosition;
          existingMove.Amount = m.Amount;
          existingMove.ReleaseTime = m.ReleaseTime;
          existingMove.DueDate = m.DueDate;
          existingMove.RequiredCraneId = m.RequiredCraneId;
          existingMove.PredecessorIds = new HashSet<int>(m.PredecessorIds);
          existingMove.MovedBlockIds = new List<int>(m.MovedBlockIds);
        } else if (sol.Schedule.Activities.Any(x => x.MoveId == m.Id)) {
          // only add custom move if it is scheduled
          World.CraneMoves.Add(m);
          _moves.Add(new CraneMoveEvent(sim, m, true));
        }
      }

      var existingMoveIds = new HashSet<int>(World.CraneMoves.Select(x => x.Id));

      // prepend active activities, remove non-existent and sort
      var schedule = sol.Schedule;
      schedule.Activities.RemoveAll(x => activeIds.Contains(x.MoveId) || !existingMoveIds.Contains(x.MoveId));
      schedule.Activities = active.Concat(schedule.Activities.OrderBy(x => x.Priority)).ToList();

      // sanity check, remove any non-existing predecessors
      foreach (var move in World.CraneMoves.Where(x => x.Predecessors > 0)) {
        move.PredecessorIds.IntersectWith(existingMoveIds);
      }

      // remove existing custom moves if they are not scheduled
      var scheduledMoveIds = new HashSet<int>(sol.Schedule.Activities.Select(x => x.MoveId));
      foreach (var move in World.CraneMoves.Where(x => x.Id >= 0).ToArray()) {
        if (move.Id >= 0 && !scheduledMoveIds.Contains(move.Id)) World.CraneMoves.Remove(move);
      }

      CraneScheduleStore.NotifyScheduleChanged(World.CraneSchedule = sol.Schedule);
    }

    private bool ValidateSolution(CraneSchedulingSolution sol) {
      if (sol.CustomMoves == null) {
        Environment.Log("custom moves is null");
        return false;
      }

      if (sol.Schedule == null) {
        Environment.Log("schedule is null");
        return false;
      }

      var isValid = true;
      var validMoveIds = new HashSet<int>();
      var invalidMoveIds = new HashSet<int>();
      var duplicateMoveIds = new HashSet<int>();
      var invalidAmountMoveIds = new HashSet<int>();
      var invalidSourceMoveIds = new HashSet<int>();
      var invalidTargetMoveIds = new HashSet<int>();

      var locations = World.Locations.ToDictionary(x => x.Id);

      foreach (var move in sol.CustomMoves) {
        if (move == null) {
          Environment.Log("custom moves contain null values");
          isValid = false;
          break;
        }

        if (move.Id < 0) invalidMoveIds.Add(move.Id);
        else if (!validMoveIds.Add(move.Id)) duplicateMoveIds.Add(move.Id);
        if (move.Amount != move.MovedBlockIds.Count
          || move.Type == MoveType.MoveToPickup && move.Amount != 0
          || move.Type == MoveType.PickupAndDropoff && move.Amount != 1
        ) {
          invalidAmountMoveIds.Add(move.Id);
        }
        if (!locations.TryGetValue(move.PickupLocationId, out var pickLoc) || pickLoc.Type == StackTypes.HandoverStack) invalidSourceMoveIds.Add(move.Id);
        if (!locations.TryGetValue(move.DropoffLocationId, out var dropLoc) || dropLoc.Type == StackTypes.ArrivalStack) invalidTargetMoveIds.Add(move.Id);
      }

      if (invalidMoveIds.Any()) {
        Environment.Log($"custom moves have invalid custom move IDs (id < 0): {string.Join(", ", invalidMoveIds.OrderBy(x => x))}");
        isValid = false;
      }

      if (duplicateMoveIds.Any()) {
        Environment.Log($"custom moves have duplicate move IDs: {string.Join(", ", duplicateMoveIds.OrderBy(x => x))}");
        isValid = false;
      }

      if (invalidAmountMoveIds.Any()) {
        Environment.Log($"custom moves have invalid move amounts: {string.Join(", ", invalidAmountMoveIds.OrderBy(x => x))}");
        isValid = false;
      }

      if (invalidSourceMoveIds.Any()) {
        Environment.Log($"custom moves have invalid move sources: {string.Join(", ", invalidSourceMoveIds.OrderBy(x => x))}");
        isValid = false;
      }

      if (invalidTargetMoveIds.Any()) {
        Environment.Log($"custom moves have invalid move targets: {string.Join(", ", invalidTargetMoveIds.OrderBy(x => x))}");
        isValid = false;
      }

      var existingCranes = World.Cranes.Select(x => x.Id);
      var scheduledCraneIds = sol.Schedule.Activities.Select(x => x.CraneId);
      var unknownCraneIds = scheduledCraneIds.Except(existingCranes).ToList();

      if (unknownCraneIds.Any()) {
        Environment.Log($"solution contains unknown cranes: {string.Join(", ", unknownCraneIds.OrderBy(x => x))}");
        isValid = false;
      }

      var craneLookup = World.Cranes.ToDictionary(x => x.Id);
      var moveLookup = World.CraneMoves.ToDictionary(x => x.Id);
      foreach (var move in sol.CustomMoves) moveLookup[move.Id] = move;

      foreach (var a in sol.Schedule.Activities) {
        if (!craneLookup.TryGetValue(a.CraneId, out var crane)) {
          Environment.Log($"solution contains invalid schedule: crane {a.CraneId} is unknown");
          isValid = false;
          break;
        }

        if (!moveLookup.TryGetValue(a.MoveId, out var move)) {
          Environment.Log($"solution contains invalid schedule: move {a.MoveId} is unknown");
          isValid = false;
          break;
        }

        if (!crane.CanReach(locations[move.PickupLocationId].GirderPosition)) {
          Environment.Log($"solution contains invalid assignments: crane {crane.Id} cannot reach pickup position {move.PickupGirderPosition} of move {move.Id}");
          isValid = false;
          break;
        }

        if (!crane.CanReach(locations[move.DropoffLocationId].GirderPosition)) {
          Environment.Log($"solution contains invalid assignments: crane {crane.Id} cannot reach dropoff position {move.DropoffGirderPosition} of move {move.Id}");
          isValid = false;
          break;
        }
      }

      return isValid;
    }

    private void GenerateUpstreamVehicle(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      var sampledCount = Environment.RandNormal(pcg, settings.HandoverCountMean, settings.HandoverCountStd);
      var blockCount = Math.Max(1, Math.Min(settings.MaxHeightForArrival, (int)Math.Round(sampledCount)));

      var blockIds = Enumerable.Range(0, blockCount).Select(x => blockId++).ToArray();
      vehicleStore.Put(blockIds);
      Environment.Log($"{Now}: new vehicle | pick | {blockIds.Length} blocks");
    }

    private void GenerateDownstreamVehicle(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      var bufferedBlocks = new HashSet<int>(World.BufferLocations.SelectMany(x => x.Stack.BottomToTop).Select(x => x.Id));
      var requestedBlockIds = new HashSet<int>(World.MoveRequests.Select(x => x.BlockId));
      var possibleBlockIds = bufferedBlocks.Except(requestedBlockIds).Except(_orderedBlockIds);

      var sampledCount = Environment.RandNormal(pcg, settings.HandoverCountMean, settings.HandoverCountStd);
      var blockCount = Math.Max(1, Math.Min(settings.MaxHeightForHandover, (int)Math.Round(sampledCount)));

      var blockIds = possibleBlockIds.Shuffle(pcg).Take(blockCount).ToArray();

      if (blockIds.Length > 0) {
        foreach (var id in blockIds) _orderedBlockIds.Add(id);
        vehicleStore.Put(blockIds);
        Environment.Log($"{Now}: new vehicle | drop | {blockIds.Length} blocks");
      }
    }

    private IEnumerable<Event> UpstreamVehicleGenerator(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      while (true) {
        yield return Environment.TimeoutLogNormal2(pcg, settings.ArrivalTimeMean, settings.ArrivalTimeStd);
        GenerateUpstreamVehicle(pcg.Next(), vehicleStore);
      }
    }

    private IEnumerable<Event> UpstreamVehicleDispatcher(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      var locsRes = new Resource(Environment, _freeArrivals.Count);

      while (true) {
        var locReq = locsRes.Request();
        yield return locReq;

        var freeLocs = _freeArrivals.OrderBy(x => x.Id).ToArray();
        var weights = freeLocs.Select(x => 1.0).ToArray();
        var locRes = Environment.RandChoice(pcg, freeLocs, weights);

        _freeArrivals.Remove(locRes);

        var vehicle = vehicleStore.Get();
        yield return vehicle;

        var blockIds = (int[])vehicle.Value;

        Environment.Log($"{Now}: dispatching | {blockIds.Length} blocks | pick location {locRes.Id}");
        _parkingTimes.Add(locRes, Now);

        foreach (var blockId in blockIds) {
          var block = new Block { Id = blockId };
          yield return locRes.Dropoff(block);
        }

        foreach (Block block in locRes.Stack.TopToBottom) {
          var req = new MoveRequest {
            Id = ++moveRequestId,
            TargetLocationId = -1,
            BlockId = block.Id,
            DueDate = new TimeStamp((long)Math.Floor(TimeSpan.MaxValue.TotalMilliseconds)),
          };

          World.MoveRequests.Add(req);
        }

        _moveGenerator.Update();

        var timeout = Environment.RandLogNormal2(pcg, settings.ArrivalServiceTimeMean, settings.ArrivalServiceTimeStd);
        Environment.Process(UpstreamVehicleProcess(locReq, locRes, timeout));
      }
    }

    private IEnumerable<Event> UpstreamVehicleProcess(Request locReq, ILocationResource locRes, TimeSpan timeout) {
      while (true) {
        var storedBefore = new HashSet<int>(locRes.Stack.BottomToTop.Select(x => x.Id));
        yield return locRes.WhenChange();
        var storedAfter = new HashSet<int>(locRes.Stack.BottomToTop.Select(x => x.Id));

        var movedBlockIds = new HashSet<int>(storedBefore.Except(storedAfter));
        World.MoveRequests.RemoveAll(x => x.TargetLocationId == -1 && movedBlockIds.Contains(x.BlockId));
        World.KPIs.UpstreamBlocks += movedBlockIds.Count;

        _moveGenerator.Update();

        if (!storedAfter.Any()) break;
      }

      Environment.Log($"{Now}: vehicle serviced @ pick location {locRes.Id}");

      var serviceTime = Now - _parkingTimes[locRes];
      _parkingTimes.Remove(locRes);
      _servicedUpstreamVehicles++;
      _upstreamServiceTime += serviceTime;
      if (serviceTime.TotalSeconds > World.KPIs.MaxParkingDuration) {
        World.KPIs.MaxParkingDuration = serviceTime.TotalSeconds;
      }

      yield return Environment.Timeout(timeout);

      _freeArrivals.Add(locRes);
      locReq.Dispose();

      Environment.Log($"{Now}: freed pick location {locRes.Id}");
    }

    private IEnumerable<Event> DownstreamVehicleGenerator(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      while (true) {
        yield return Environment.TimeoutLogNormal2(pcg, settings.HandoverTimeMean, settings.HandoverTimeStd);
        GenerateDownstreamVehicle(pcg.Next(), vehicleStore);
      }
    }

    private IEnumerable<Event> DownstreamVehicleDispatcher(int seed, Store vehicleStore) {
      var pcg = new PcgRandom(seed);

      var locsRes = new Resource(Environment, _freeHandovers.Count);

      while (true) {
        var locReq = locsRes.Request();
        yield return locReq;

        var freeLocs = _freeHandovers.OrderBy(x => x.Id).ToArray();
        var weights = freeLocs.Select(x => 1.0).ToArray();
        var locRes = Environment.RandChoice(pcg, freeLocs, weights);

        _freeHandovers.Remove(locRes);

        var vehicle = vehicleStore.Get();
        yield return vehicle;

        var blockIds = (int[])vehicle.Value;

        Environment.Log($"{Now}: dispatching | {blockIds.Length} blocks | drop location {locRes.Id}");
        _parkingTimes.Add(locRes, Now);

        foreach (var blockId in blockIds) {
          var req = new MoveRequest {
            Id = ++moveRequestId,
            TargetLocationId = locRes.Id,
            BlockId = blockId,
            DueDate = new TimeStamp((long)Math.Floor(TimeSpan.MaxValue.TotalMilliseconds)),
          };

          World.MoveRequests.Add(req);
        }

        foreach (var id in blockIds) _orderedBlockIds.Remove(id);

        _moveGenerator.Update();

        var timeout = Environment.RandLogNormal2(pcg, settings.HandoverServiceTimeMean, settings.HandoverServiceTimeStd);
        Environment.Process(DownstreamVehicleProcess(locReq, locRes, blockIds, timeout));
      }
    }

    private IEnumerable<Event> DownstreamVehicleProcess(Request locReq, ILocationResource locRes, int[] requiredBlockIds, TimeSpan timeout) {
      while (true) {
        var storedBefore = new HashSet<int>(locRes.Stack.BottomToTop.Select(x => x.Id));
        yield return locRes.WhenChange();
        var storedAfter = new HashSet<int>(locRes.Stack.BottomToTop.Select(x => x.Id));

        var movedBlockIds = new HashSet<int>(storedAfter.Except(storedBefore));
        World.MoveRequests.RemoveAll(x => x.TargetLocationId != -1 && movedBlockIds.Contains(x.BlockId));
        World.KPIs.DownstreamBlocks += movedBlockIds.Count;
        World.KPIs.DeliveryErrors += movedBlockIds.Except(requiredBlockIds).Count();

        _moveGenerator.Update();

        if (storedAfter.Count >= requiredBlockIds.Length) {
          Environment.Log($"{Now}: vehicle serviced @ drop location {locRes.Id}");

          var serviceTime = Now - _parkingTimes[locRes];
          _parkingTimes.Remove(locRes);
          _servicedDownstreamVehicles++;
          _downstreamServiceTime += serviceTime;
          if (serviceTime.TotalSeconds > World.KPIs.MaxParkingDuration) {
            World.KPIs.MaxParkingDuration = serviceTime.TotalSeconds;
          }

          yield return locRes.Pickup(storedAfter.Count);
          yield return Environment.Timeout(timeout);

          _freeHandovers.Add(locRes);
          locReq.Dispose();

          Environment.Log($"{Now}: freed drop location {locRes.Id}");
          break;
        }
      }
    }

    private IEnumerable<Event> CountManipulations(ICraneAgent agent) {
      while (true) {
        yield return agent.WhenPickupOrDropoff();
        World.KPIs.CraneManipulations++;
      }
    }

    private bool _locked = false;
    private (double lower, double upper) _lock;

    private IEnumerable<Event> LockArea() {
      while (true) {
        yield return Environment.Timeout(TimeSpan.FromMinutes(5));
        double a, b;
        do {
          a = Environment.RandUniform(0.1, 0.9);
          b = Environment.RandUniform(0.1, 0.9);
        } while (Math.Abs(a - b) > 0.5 || Math.Abs(a - b) < 0.1);
        _lock = (Math.Min(a, b) * World.Width, Math.Max(a, b) * World.Width);
        using (var req = _zoneControl.Request(_lock.lower, _lock.upper)) {
          yield return req;
          _locked = true;
          yield return Environment.Timeout(TimeSpan.FromMinutes(5));
          _locked = false;
        }
      }
    }

    private IEnumerable<Event> SpaceTimeDrawing() {
      var alt = true;
      while (true) {
        var sb = new StringBuilder();
        var lockStartPrinted = !_locked;
        var lockStart = (int)Math.Floor(_lock.lower / World.Width * 100);
        var lockEndPrinted = !_locked;
        var lockEnd = (int)Math.Floor(_lock.upper / World.Width * 100);
        var padChar = ' ';
        foreach (var m in _agents.OrderBy(x => x.GetGirderPosition())) {
          if (!lockStartPrinted && _locked && _lock.lower < m.GetGirderPosition()) {
            if (lockStart <= sb.Length) sb.Append('x');
            else sb.Append("x".PadLeft(lockStart - sb.Length));
            padChar = 'x';
            lockStartPrinted = true;
          }
          if (!lockEndPrinted && _locked && _lock.upper <= m.GetGirderPosition()) {
            if (lockEnd <= sb.Length) sb.Append('x');
            else sb.Append("x".PadLeft(lockEnd - sb.Length, 'x'));
            padChar = ' ';
            lockEndPrinted = true;
          }
          var symbol = alt ? "|" : m.Id.ToString();
          if (m.State == CraneAgentState.Waiting) symbol = alt ? "!" : m.Id.ToString();
          else if (m.State == CraneAgentState.Dropping) symbol = alt ? "v" : m.Id.ToString();
          else if (m.State == CraneAgentState.Picking) symbol = alt ? "^" : m.Id.ToString();
          else {
            if (m.Direction == CraneAgentDirection.ToLower) symbol = alt ? "<" : m.Id.ToString();
            else if (m.Direction == CraneAgentDirection.ToUpper) symbol = alt ? ">" : m.Id.ToString();
          }
          var pos = (int)Math.Floor(m.GetGirderPosition() / World.Width * 100);
          if (pos <= sb.Length) sb.Append(symbol);
          else sb.Append(symbol.PadLeft(pos - sb.Length, padChar));
        }
        if (!lockEndPrinted && _locked) {
          if (!lockStartPrinted) {
            if (lockStart <= sb.Length) sb.Append('x');
            else sb.Append("x".PadLeft(lockStart - sb.Length));
          }
          if (lockEnd <= sb.Length) sb.Append('x');
          else sb.Append("x".PadLeft(lockEnd - sb.Length, 'x'));
        }
        Environment.Log(sb.ToString().PadRight(100));
        /*sb.Clear();
        sb.Append(Now.ToString());
        foreach (var m in _agents.OrderBy(x => x.GetGirderPosition())) {
          if (m.State != CraneAgentState.Moving) sb.Append($" {m.Id}: {m.GetGirderPosition():F1}");
          else {
            if (m.TargetPosition == m.GoalPosition1)
              sb.Append($" {m.Id}: {m.GetGirderPosition():F1}->{m.TargetPosition:F1}");
            else sb.Append($" {m.Id}: {m.GetGirderPosition():F1}->{m.TargetPosition:F1}/{m.GoalPosition1:F1}");
          }
        }
        Environment.Log(sb.AppendLine().ToString());*/
        alt = !alt;
        yield return Environment.Timeout(TimeSpan.FromSeconds(1));
      }
    }

    public World GetEstimatedWorldState() {
      if (World == null) return World;

      World.Now = Now;

      foreach (var agent in CraneAgents) {
        agent.GetGirderPosition(); // will cause an update
      }

      World.KPIs.TotalGirderDistance = CraneAgents.Sum(x => x.GetGirderDistance());
      World.KPIs.TotalHoistDistance = CraneAgents.Sum(x => x.GetHoistDistance());

      World.KPIs.UpstreamServiceTime = _upstreamServiceTime.TotalSeconds;
      World.KPIs.ServicedUpstreamVehicles = _servicedUpstreamVehicles;
      World.KPIs.DownstreamServiceTime = _downstreamServiceTime.TotalSeconds;
      World.KPIs.ServicedDownstreamVehicles = _servicedDownstreamVehicles;

      var upstreamParkingTime = TimeSpan.Zero;
      var parkingUpstreamVehicles = 0;

      var downstreamParkingTime = TimeSpan.Zero;
      var parkingDownstreamVehicles = 0;

      foreach (var parked in _parkingTimes) {
        var parkingDuration = Now - parked.Value;

        switch (_locationLookup[parked.Key].Type) {
          case StackTypes.ArrivalStack:
            upstreamParkingTime += parkingDuration;
            parkingUpstreamVehicles++;
            break;
          case StackTypes.HandoverStack:
            downstreamParkingTime += parkingDuration;
            parkingDownstreamVehicles++;
            break;
        }

        if (parkingDuration.TotalSeconds > World.KPIs.MaxParkingDuration) {
          World.KPIs.MaxParkingDuration = parkingDuration.TotalSeconds;
        }
      }

      World.KPIs.UpstreamParkingTime = upstreamParkingTime.TotalSeconds;
      World.KPIs.ParkingUpstreamVehicles = parkingUpstreamVehicles;
      World.KPIs.DownstreamParkingTime = downstreamParkingTime.TotalSeconds;
      World.KPIs.ParkingDownstreamVehicles = parkingDownstreamVehicles;

      return World;
    }

    public event EventHandler WorldChanged;
    private IEnumerable<Event> WorldUpdates() {
      while (true) {
        WorldChanged?.Invoke(this, EventArgs.Empty);
        yield return Environment.Timeout(TimeSpan.FromSeconds(1.0));
      }
    }

    public void MoveFinished(int moveId, int craneId, TimeStamp started, (int, int, int) hoistDistances, CraneMoveTermination result) {
      var m = World.CraneMoves.Select((mv, idx) => (mv, idx)).SingleOrDefault(x => x.mv.Id == moveId);
      if (m.mv == null) return; // TODO: check why this is invoked twice sometimes (interrupts?)
      var index = m.idx;
      if (World.CraneMoves[index].Id != _moves[index].Id) throw new InvalidOperationException("Moves not in sync.");
      _moves.RemoveAt(index);
      World.CraneMoves.RemoveAt(index);
      _moveGenerator.Update();
      Environment.Log($"{Now}: move {moveId} removed from index {index} (finished by crane {craneId})");
      _zoneControl.MoveUpdate();
    }

    public int HeightBetween(double pos1, double pos2) {
      var min = Math.Min(pos1, pos2);
      var max = Math.Max(pos1, pos2);
      var maxHeight = 0;
      foreach (var loc in _locations.Where(x => x.GirderPosition >= min && x.GirderPosition <= max)) {
        maxHeight = Math.Max(maxHeight, loc.Height);
      }
      return maxHeight;
    }
  }
}
