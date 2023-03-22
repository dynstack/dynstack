using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DynStack.DataModel;
using DynStack.DataModel.RM;
using SimSharp;
using Simulation.Util;

namespace DynStack.Simulation.RM {
  public enum MoveCondition {
    Valid,
    InvalidBlockId,
    InvalidStack,
    HeightLimitViolated,
    NotEnoughBlocks
  }
  public class RollingMillSimulation : IStackingEnvironment {
    public World World { get; private set; }
    private PcgRandom arrivalRng;
    private PcgRandom programRng;
    private Dictionary<MillTypes, (PriorityStore prog, Process proc)> rollingProgram;

    private Settings settings { get; }

    private IPolicy _policy;

    private PseudoRealtimeSimulation sim;
    public SimSharp.Simulation Environment => sim;
    public TimeStamp Now => sim.NowTS();
    public int Height => World.Height;
    public double Width => World.Width;

    private List<ILocationResource> _locations;
    private List<ICraneAgent> _craneAgents;
    private List<ICraneMoveEvent> _moveEvents;
    private ICraneScheduleStore _scheduleStore;
    private IZoneControl _zoneControl;
    private ICraneScheduler _scheduler;
    IEnumerable<ILocationResource> IStackingEnvironment.LocationResources => _locations;
    IEnumerable<IBlock> IStackingEnvironment.Blocks => World.AllBlocks();
    IEnumerable<ICraneAgent> IStackingEnvironment.CraneAgents => _craneAgents;
    IEnumerable<ICraneMoveEvent> IStackingEnvironment.CraneMoves => _moveEvents;
    IEnumerable<IMoveRequest> IStackingEnvironment.MoveRequests => World.MoveRequests;

    private IReadOnlyList<ILocationResource> shuffleBuffers, sortedBuffers;

    public ICraneScheduleStore CraneScheduleStore => _scheduleStore;

    public IZoneControl ZoneControl => _zoneControl;

    public SampleMonitor ServiceLevel { get; private set; }
    public SampleMonitor LeadTimes { get; private set; }
    public SampleMonitor Tardiness { get; private set; }
    public SampleMonitor ArrivalStacksWaitingTime { get; private set; }
    public TimeSeriesMonitor ShuffleBufferUtilization { get; private set; }
    public TimeSeriesMonitor SortedBufferUtilization { get; private set; }
    public TimeSeriesMonitor ShuffleCraneUtilization { get; private set; }
    public TimeSeriesMonitor HandoverCraneUtilization { get; private set; }
    public Dictionary<MillTypes, TimeSeriesMonitor> MillUtilization { get; private set; }

    public void SetLogger(TextWriter logger) {
      sim.Logger = logger;
    }

    public RollingMillSimulation(Settings set) {
      settings = set;
      sim = new PseudoRealtimeSimulation(DateTime.UtcNow, settings.Seed);
      ServiceLevel = new SampleMonitor("SerivceLevel");
      LeadTimes = new SampleMonitor("LeadTimes");
      Tardiness = new SampleMonitor("Tardiness");
      ArrivalStacksWaitingTime = new SampleMonitor("ArrivalStacksWaitingTime");
      ShuffleCraneUtilization = new TimeSeriesMonitor(sim, "ShuffleBufferUtilization");
      SortedBufferUtilization = new TimeSeriesMonitor(sim, "SortedBufferUtilization");
      ShuffleCraneUtilization = new TimeSeriesMonitor(sim, "ShuffleCraneUtilization");
      HandoverCraneUtilization = new TimeSeriesMonitor(sim, "HandoverCraneUtilization");
      MillUtilization = new Dictionary<MillTypes, TimeSeriesMonitor>() {
        { MillTypes.A, new TimeSeriesMonitor(sim, "MillAUtilization") },
        { MillTypes.B, new TimeSeriesMonitor(sim, "MillBUtilization") }
      };

      World = new World {
        Width = settings.Width,
        Height = settings.Height,
        Locations = new List<Location>(),
        BlocksAtSlabYard = new List<Block>(),
        ArrivalsFromSlabYard = new List<Arrival>(),
        CraneMoves = new PlannedCraneMoves(),
        CraneSchedule = new CraneSchedule(),
        MoveRequests = new List<MoveRequest>(),
        ShuffleCrane = new Crane {
          Id = 1,
          CraneCapacity = set.CraneCapacity,
          Load = new Stack(),
          HoistLevel = settings.Height,
          GirderPosition = 1,
          Width = set.SafetyDistance,
        },
        HandoverCrane = new Crane {
          Id = 2,
          CraneCapacity = set.CraneCapacity,
          Load = new Stack(),
          HoistLevel = settings.Height,
          GirderPosition = settings.Width - 1,
          Width = set.SafetyDistance,
        },
        Now = new TimeStamp(0),
        KPIs = new Performance(),
        ObservationData = new Uncertainties()
      };

      var stackId = 0;
      foreach (var pos in set.ArrivalStackPositions)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.ArrivalStack,
          GirderPosition = pos,
          MaxHeight = set.MaxHeightForArrival,
          Stack = new Stack(),
          MillType = null
        });
      foreach (var pos in set.ShuffleStackPositions)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.ShuffleBuffer,
          GirderPosition = pos,
          MaxHeight = set.MaxHeightForShuffleBuffer,
          Stack = new Stack(),
          MillType = null
        });
      foreach (var pos in set.SortedStackPositions)
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.SortedBuffer,
          GirderPosition = pos,
          MaxHeight = set.MaxHeightForSortedBuffer,
          Stack = new Stack(),
          MillType = null
        });
      foreach (var kvp in set.HandoverStackPositions) {
        World.Locations.Add(new Location() {
          Id = stackId++,
          Type = StackTypes.HandoverStack,
          GirderPosition = kvp.Value,
          MaxHeight = set.MaxHeightAtHandover,
          Stack = new Stack(),
          MillType = kvp.Key
        });
      }

      World.ShuffleCrane.MinPosition = World.Locations.Where(x => x.Type != StackTypes.HandoverStack).Min(x => x.GirderPosition);
      World.ShuffleCrane.MaxPosition = World.Locations.Where(x => x.Type != StackTypes.HandoverStack).Max(x => x.GirderPosition);
      World.ShuffleCrane.GirderPosition = World.ShuffleCrane.MinPosition + 1;

      World.HandoverCrane.MinPosition = World.Locations.Where(x => x.Type != StackTypes.ArrivalStack).Min(x => x.GirderPosition);
      World.HandoverCrane.MaxPosition = World.Locations.Where(x => x.Type != StackTypes.ArrivalStack).Max(x => x.GirderPosition);
      World.HandoverCrane.GirderPosition = World.HandoverCrane.MaxPosition - 1;
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

      OnWorldChanged();
      await sim.RunAsync(settings.SimulationDuration);

      OnWorldChanged(kpichange: true);

      return World;
    }

    private void Initialize() {
      programRng = new PcgRandom(settings.Seed + 1);

      var meanGSpeedShuffle = settings.Width / settings.CraneMoveTimeMean.TotalSeconds;
      var cvGSpeedShuffle = settings.CraneMoveTimeStd.TotalSeconds / settings.CraneMoveTimeMean.TotalSeconds;
      var meanHSpeedShuffle = settings.Height / settings.HoistMoveTimeMean.TotalSeconds;
      var cvHSpeedShuffle = settings.HoistMoveTimeStd.TotalSeconds / settings.HoistMoveTimeMean.TotalSeconds;
      _craneAgents = new List<ICraneAgent>() {
        new CraneAgent(this, World.ShuffleCrane,
          new LognormalDistribution(sim, meanGSpeedShuffle, meanGSpeedShuffle * cvGSpeedShuffle),
          new LognormalDistribution(sim, meanHSpeedShuffle, meanHSpeedShuffle * cvHSpeedShuffle),
          new LognormalDistribution(sim, settings.CraneManipulationTimeMean.TotalSeconds, settings.CraneManipulationTimeStd.TotalSeconds)) {
          Utilization = ShuffleCraneUtilization
        },
        new CraneAgent(this, World.HandoverCrane,
          new LognormalDistribution(sim, meanGSpeedShuffle, meanGSpeedShuffle * cvGSpeedShuffle),
          new LognormalDistribution(sim, meanHSpeedShuffle, meanHSpeedShuffle * cvHSpeedShuffle),
          new LognormalDistribution(sim, settings.CraneManipulationTimeMean.TotalSeconds, settings.CraneManipulationTimeStd.TotalSeconds)) {
          Utilization = HandoverCraneUtilization
        }
      };

      _locations = new List<ILocationResource>();
      var arrivalLocs = new List<ILocationResource>();
      var shuffleLocs = new List<ILocationResource>();
      var sortedLocs = new List<ILocationResource>();
      foreach (var loc in World.Locations) {
        var l = new LocationResource(this, loc) { Utilization = new TimeSeriesMonitor(sim) };
        _locations.Add(l);
        if (loc.Type == StackTypes.ArrivalStack) arrivalLocs.Add(l);
        else if (loc.Type == StackTypes.ShuffleBuffer) shuffleLocs.Add(l);
        else if (loc.Type == StackTypes.SortedBuffer) sortedLocs.Add(l);
      }
      shuffleBuffers = shuffleLocs.AsReadOnly();
      sortedBuffers = sortedLocs.AsReadOnly();

      arrivalRng = new PcgRandom(settings.Seed + 2);

      _moveEvents = new List<ICraneMoveEvent>();
      foreach (var move in World.CraneMoves?.Moves??Enumerable.Empty<CraneMove>()) {
        _moveEvents.Add(new CraneMoveEvent(Environment, move));
      }

      _scheduler = new BasicCraneScheduler(this);

      _scheduleStore = new CraneScheduleStore(this, World.CraneSchedule);
      _zoneControl = new ZoneControl(this);

      sim.Process(ProgramGenerator());
      rollingProgram = new Dictionary<MillTypes, (PriorityStore, Process)>() {
        { MillTypes.A, (new PriorityStore(sim), sim.Process(MillProcess(MillTypes.A))) },
        { MillTypes.B, (new PriorityStore(sim), sim.Process(MillProcess(MillTypes.B))) }
      };
      sim.Process(ArrivalProcess());
      sim.Process(PolicyProcess(TimeSpan.FromSeconds(5)));
      sim.Process(SchedulerProcess(TimeSpan.FromSeconds(1)));

      // Initialization phase
      var oldPolicy = _policy;
      _policy = new BasicSortingPolicy();
      sim.SetVirtualtime();
      sim.Run(settings.InitialPhase);
      _policy = oldPolicy;
      if (_policy == null) sim.SetRealtime();

      sim.Process(WorldUpdates());
      foreach (var agent in _craneAgents) sim.Process(CountManipulations(agent));

      ServiceLevel.Reset();
      LeadTimes.Reset();
      Tardiness.Reset();
      ArrivalStacksWaitingTime.Reset();
      ShuffleCraneUtilization.Reset();
      SortedBufferUtilization.Reset();
      ShuffleCraneUtilization.Reset();
      HandoverCraneUtilization.Reset();
      MillUtilization[MillTypes.A].Reset();
      MillUtilization[MillTypes.B].Reset();

      World.KPIs.ShuffleCraneUtilizationMean = 0;
      World.KPIs.HandoverCraneUtilizationMean = 0;
      World.KPIs.ShuffleBufferUtilizationMean = 0;
      World.KPIs.SortedBufferUtilizationMean = 0;
      World.KPIs.MillAUtilizationMean = 0;
      World.KPIs.MillBUtilizationMean = 0;
    }

    private IEnumerable<Event> CountManipulations(ICraneAgent agent) {
      var lastAction = Now;
      while (true) {
        yield return agent.WhenPickupOrDropoff();
        if (agent.State == CraneAgentState.Picking) lastAction = Now;
        else {
          World.ObservationData.CraneMoveTimes.AddLast((Now - lastAction).TotalSeconds);
          if (World.ObservationData.CraneMoveTimes.Count > 30)
            World.ObservationData.CraneMoveTimes.RemoveFirst();
          lastAction = Now; // just in case there's subsequent drop-offs
        }
        World.KPIs.CraneManipulations++;
        OnWorldChanged(kpichange: true);
      }
    }

    public void StopAsync() {
      sim.StopAsync();
    }
    /// <summary>
    /// In this mode the simulation uses an integrated policy that is called directly via C# method invocation
    /// and does not need to interact asynchronously via network. This is not a realistic case, as the simulation
    /// will pause and await the policy's result, and thus also runs in virtual time.
    /// </summary>
    /// <param name="set">The settings that the simulation should run with.</param>
    /// <param name="policy">The policy that handles the crane orders.</param>
    public RollingMillSimulation(Settings set, IPolicy policy) : this(set) {
      this._policy = policy;
      sim.SetVirtualtime();
    }

    public async Task SetPlannedMovesAsync(PlannedCraneMoves moves) {
      await Task.Run(() => {
        sim.Process(SetPlannedMovesWithDelay(TimeSpan.Zero, moves));
      });
    }
    private IEnumerable<Event> SchedulerProcess(TimeSpan updateInterval) {
      while (true) {
        yield return sim.Timeout(updateInterval);
        _scheduler?.Schedule();
      }
    }

    private IEnumerable<Event> PolicyProcess(TimeSpan updateInterval) {
      while (true) {
        yield return sim.Timeout(updateInterval);
        if (_policy == null) yield break;
        GetEstimatedWorldState();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var moves = _policy?.CalculateMoves(World);
        sw.Stop();
        if (moves != null && moves.Count > 0) sim.Process(SetPlannedMovesWithDelay(sw.Elapsed, moves));
      }
    }

    int nxtProgId;

    // Generates new blocks batched to rolling programs
    private IEnumerable<Event> ProgramGenerator() {
      var millBlocks = new Dictionary<MillTypes, int> { { MillTypes.A, 0 }, { MillTypes.B, 0 } };
      int blockIds = World.AllBlocks().Any() ? World.AllBlocks().Max(x => x.Id) + 1 : 1;
      nxtProgId = World.AllBlocks().Any() ? World.AllBlocks().Max(x => x.ProgramId) : 1;
      foreach (var block in World.AllBlocks().OrderBy(x => x.Sequence).ToList()) {
        var prio = millBlocks[block.Type] + 1;
        rollingProgram[block.Type].prog.Put(block, prio);
        millBlocks[block.Type] = prio;
      }
      while (true) {
        var programSize = programRng.Next(settings.ProgramSizeMin, settings.ProgramSizeMax);
        foreach (var block in CreateBlocks(programSize, ref blockIds)) {
          block.ProgramId = nxtProgId;
          block.Sequence = World.AllBlocks().Any(x => x.Type == block.Type) ? World.AllBlocks().Where(x => x.Type == block.Type).Max(x => x.Sequence) + 1: 1;
          World.BlocksAtSlabYard.Add(block);
          var prio = millBlocks[block.Type] + 1;
          yield return rollingProgram[block.Type].prog.Put(block, prio);
          millBlocks[block.Type] = prio;
          sim.Log("{0} New block created {1} (program {2}, mill {3}), seq {4}", Now, block.Id, block.ProgramId, block.Type, block.Sequence);
        }
        while (World.AllBlocks().Select(x => x.ProgramId).Distinct().Count() >= settings.ProgramCount) {
          yield return rollingProgram[MillTypes.A].prog.WhenChange() | rollingProgram[MillTypes.B].prog.WhenChange();
        }
        OnWorldChanged();
        nxtProgId++;
      }
    }

    private Block[] CreateBlocks(int programSize, ref int startId) {
      var type = programRng.NextDouble() < 0.5 ? MillTypes.A : MillTypes.B;
      var program = new Block[programSize];
      for (var p = 0; p < programSize; p++) {
        program[p] = new Block() {
          Id = startId++,
          Type = type,
          Rolled = false
        };
        if (type == MillTypes.A) type = MillTypes.B;
        else type = MillTypes.A;
      }
      return program;
    }

    private IEnumerable<Event> ArrivalProcess() {
      var arrivalStacks = World.Locations.Where(x => x.Type == StackTypes.ArrivalStack)
        .Select(x => _locations.Single(y => y.Id == x.Id)).ToList();
      while (true) {
        var arrivalStart = World.Now;
        yield return rollingProgram[MillTypes.A].prog.WhenAny() | rollingProgram[MillTypes.B].prog.WhenAny();

        LinkedList<Block> pool;
        // Create the pool of deliverable slabs
        do {
          pool = new LinkedList<Block>(World.BlocksAtSlabYard.OrderBy(x => x.Sequence).Take(settings.ArrivalSequencePoolSize));
          if (pool.Count == 0) {
            yield return rollingProgram[MillTypes.A].prog.WhenChange() | rollingProgram[MillTypes.B].prog.WhenChange();
          } else break;
        } while (true);

        var lotSize = Enumerable.Range(1, settings.ArrivalLotSizeWeights.Count)
                                .SampleProportional(arrivalRng, settings.ArrivalLotSizeWeights, false, false).First();
        var stack = new List<Block>(lotSize);
        stack.Add(pool.First()); // the next in sequence is always transported
        pool.RemoveFirst();
        if (lotSize > 1) {
          // weed out some blocks from the other mill from the pool
          var remaining = pool.Count;
          for (var b = pool.Last; remaining > 0; b = b?.Previous ?? pool.Last) {
            if (pool.Count <= lotSize) break;
            if (b.Value.Type != stack[0].Type && arrivalRng.NextDouble() < settings.ArrivalMillPurity) {
              var tmp = b.Next;
              pool.Remove(b);
              b = tmp;
            }
            remaining--;
          }
          // randomly add further blocks to the lot from the pool
          remaining = pool.Count;
          for (var b = pool.First; remaining > 0; b = b?.Next ?? pool.First) {
            if (arrivalRng.NextDouble() < (lotSize - stack.Count) / (double)remaining) {
              stack.Add(b.Value);
              var tmp = b.Previous;
              pool.Remove(b);
              b = tmp;
            }
            remaining--;
            if (stack.Count == lotSize) break;
          }
        }

        var chosen = new Stack() {
          BottomToTop = stack.Shuffle(arrivalRng).ToList()
        };

        var timeout = sim.RandLogNormal2(arrivalRng, settings.ArrivalTimeMean, settings.ArrivalTimeStd);
        foreach (var b in chosen.BottomToTop) World.BlocksAtSlabYard.Remove(b);

        var delivery = new Arrival() {
          Vehicle = 1,
          ArrivalEstimate = Now + timeout,
          Load = chosen
        };
        World.ArrivalsFromSlabYard.Add(delivery);

        OnWorldChanged();

        yield return sim.Timeout(timeout);
        ILocationResource loc;
        // attempt to deliver to the arrival stack
        while (true) {
          // first try to acquire an arrival stack that can accomodate the load
          while (true) {
            loc = arrivalStacks.Shuffle(arrivalRng).FirstOrDefault(x => x.FreeHeight >= delivery.Load.Size);
            if (loc != null) break;
            // otherwise wait until something changes in those stacks
            yield return new AnyOf(sim, arrivalStacks.Select(x => x.WhenChange()));
          }
          // then request ZoC in order to avoid collision with cranes while unloading
          using (var zoc = ZoneControl.Request(Math.Max(0, loc.GirderPosition - 1), Math.Min(World.Width, loc.GirderPosition + 1))) {
            yield return zoc;
            // have to check again whether we can actually unload -> otherwise we'll have to give up ZoC again
            if (loc.FreeHeight < delivery.Load.Size) continue;

            // a timeout to perform the unloading
            yield return Environment.TimeoutTriangular(settings.ArrivalUnloadTimeMin, settings.ArrivalUnloadTimeMax);
            yield return loc.Dropoff(delivery.Load);
            foreach (var block in delivery.Load.BottomToTop)
              block.Arrived = Now;
            sim.Log("{0} New arrival: {1} blocks (({2})).", Now, delivery.Load.Size, string.Join("), (", delivery.Load.BottomToTop.Select(x => x.Id)));
            World.ArrivalsFromSlabYard.Remove(delivery);
            OnWorldChanged();
          }
          break;
        }
        World.ObservationData.ArrivalIntervals.AddLast((World.Now - arrivalStart).TotalSeconds);
        if (World.ObservationData.ArrivalIntervals.Count > 30)
          World.ObservationData.ArrivalIntervals.RemoveFirst();
      }
    }

    private int moveRequestIds = 1;
    private Event programFinished = null;

    private IEnumerable<Event> MillProcess(MillTypes type) {
      var handoverId = World.Locations.Single(x => x.MillType == type && x.Type == StackTypes.HandoverStack).Id;
      var handover = _locations.Single(x => x.Id == handoverId);
      var progId = 0;
      yield return sim.Timeout(settings.InitialPhase); // initialization phase
      while (true) {
        var handoverStart = World.Now;
        MillUtilization[type].UpdateTo(0);
        var nextinwp = rollingProgram[type].prog.Get();
        yield return nextinwp; // pulls the next block from the program
        sim.ActiveProcess.HandleFault(); // nothing to do, the erroneous block will be detected as being rolled already
        var nextblockinwp = (Block)nextinwp.Value;
        if (nextblockinwp.Rolled) {
          continue;
        }
        MillUtilization[type].UpdateTo(1);

        var interval = TimeSpan.Zero;
        if (nextblockinwp.ProgramId != progId) {
          // Mills need to synchronize
          if (programFinished == null) {
            yield return programFinished = new Event(sim);
            if (sim.ActiveProcess.HandleFault()) {
              programFinished = null;
              continue;
            }
          } else {
            programFinished.Succeed();
            programFinished = null;
            interval = sim.RandTriangular(programRng, settings.ProgramBlockIntervalMin, settings.ProgramBlockIntervalMax);
          }
          interval += sim.RandTriangular(programRng, settings.ProgramIntervalMin, settings.ProgramIntervalMax);
        } else interval += sim.RandTriangular(programRng, settings.ProgramBlockIntervalMin, settings.ProgramBlockIntervalMax);
        progId = nextblockinwp.ProgramId;

        if (handover.Height == 0 || handover.Topmost.Id != nextblockinwp.Id) {
          World.MoveRequests.Add(new MoveRequest() {
            BlockId = nextblockinwp.Id,
            DueDate = Now + interval,
            TargetLocationId = handoverId,
            Id = moveRequestIds++
          });
        }
        OnWorldChanged();

        yield return sim.Timeout(interval);
        if (sim.ActiveProcess.HandleFault()) {
          World.MoveRequests.RemoveAll(x => x.BlockId == nextblockinwp.Id);
          OnWorldChanged();
          continue;
        }
        var start = Now;
        var nextonhandover = handover.Pickup();
        MillUtilization[type].UpdateTo(0);
        yield return nextonhandover;
        if (sim.ActiveProcess.HandleFault()) {
          World.MoveRequests.RemoveAll(x => x.BlockId == nextblockinwp.Id);
          nextonhandover.Cancel();
          OnWorldChanged();
          continue;
        }
        MillUtilization[type].UpdateTo(1);
        var tardy = Now > start;
        if (tardy) {
          World.KPIs.BlockedMillTime += (Now - start).TotalSeconds;
          Tardiness.Add((Now - start).TotalSeconds);
          World.KPIs.TardinessMean = Tardiness.Mean;
        }

        var nextblockonhandover = (Block)nextonhandover.Block;
        if (nextblockonhandover.Id != nextblockinwp.Id) {
          sim.Log("{0} Mill {1} CRITICAL: rolling program {2}, wrong block, expected ({3}), received ({4}).", Now, type, nextblockinwp.ProgramId, nextblockinwp.Id, nextblockonhandover.Id);
          if (nextblockonhandover.Type != type) {
            // wrong Mill!
            World.KPIs.RollingProgramMessups += 10;
            if (nextblockonhandover.Sequence == 1) {
              // other mill process needs to be interrupted
              var otherMill = type == MillTypes.A ? MillTypes.B : MillTypes.A;
              rollingProgram[otherMill].proc.Interrupt();
            }
          } else World.KPIs.RollingProgramMessups++;
          ServiceLevel.Add(0);
          rollingProgram[type].prog.Put(nextblockinwp, 1);
        } else {
          if (tardy) ServiceLevel.Add(0);
          else {
            ServiceLevel.Add(1);
            World.KPIs.TotalBlocksOnTime++;
          }
        }
        LeadTimes.Add((Now - nextblockonhandover.Arrived.Value).TotalSeconds);
        nextblockonhandover.Rolled = true;
        World.MoveRequests.RemoveAll(x => x.BlockId == nextblockinwp.Id);
        World.KPIs.ServiceLevelMean = ServiceLevel.Mean;
        World.KPIs.DeliveredBlocks++;
        World.KPIs.LeadTimeMean = LeadTimes.Mean;

        foreach (var block in World.AllBlocks().Where(x => x.Type == nextblockonhandover.Type && x.Sequence > nextblockonhandover.Sequence)) {
          block.Sequence--;
        }

        World.ObservationData.MillBlockIntervals.AddLast((World.Now - handoverStart).TotalSeconds);
        if (World.ObservationData.MillBlockIntervals.Count > 30)
          World.ObservationData.MillBlockIntervals.RemoveFirst();

        OnWorldChanged();
      }
    }

    public World GetEstimatedWorldState() {
      if (World == null) return World;

      World.Now = Now;
      World.KPIs.ShuffleCraneUtilizationMean = ShuffleCraneUtilization.Mean;
      World.KPIs.HandoverCraneUtilizationMean = HandoverCraneUtilization.Mean;
      World.KPIs.ShuffleBufferUtilizationMean = shuffleBuffers.Sum(l => l.Utilization.Mean * l.MaxHeight) / shuffleBuffers.Sum(l => l.MaxHeight);
      World.KPIs.SortedBufferUtilizationMean = sortedBuffers.Sum(l => l.Utilization.Mean * l.MaxHeight) / sortedBuffers.Sum(l => l.MaxHeight);
      World.KPIs.MillAUtilizationMean = MillUtilization[MillTypes.A].Mean;
      World.KPIs.MillBUtilizationMean = MillUtilization[MillTypes.B].Mean;

      foreach (var agent in _craneAgents) {
        agent.GetGirderPosition(); // will cause an update
      }

      return World;
    }

    private IEnumerable<Event> SetPlannedMovesWithDelay(TimeSpan delay, PlannedCraneMoves moves) {
      yield return sim.Timeout(delay);
      World.CraneMoves.SequenceNr = moves.SequenceNr;
      var change = false;
      var set = new HashSet<int>(World.CraneSchedule.Activities.Select(x => x.MoveId)); // scheduled activities may not be removed anymore
      var idToPos = World.Locations.ToDictionary(x => x.Id, x => x.GirderPosition);
      foreach (var m in moves.Moves) {
        if (m.Type == MoveType.MoveToPickup) m.DropoffLocationId = m.PickupLocationId;
        if (idToPos.TryGetValue(m.PickupLocationId, out var ppos))
          m.PickupGirderPosition = ppos;
        else continue; // ignore the move
        if (idToPos.TryGetValue(m.DropoffLocationId, out var dpos))
          m.DropoffGirderPosition = dpos;
        else continue; // ignore the move
        set.Add(m.Id);
        if (_moveEvents.Any(x => x.Id == m.Id)) continue;
        World.CraneMoves.Moves.Add(m);
        _moveEvents.Add(new CraneMoveEvent(sim, m));
        change = true;
      }
      foreach (var m in _moveEvents.Select((v, i) => (move: v, index: i)).Where(x => !set.Contains(x.move.Id)).ToList()) {
        _moveEvents.RemoveAt(m.index);
        World.CraneMoves.Moves.RemoveAt(m.index);
        change = true;
      }
      // sanity check, remove any non-existing predecessors
      foreach (var m in World.CraneMoves.Moves.Where(x => x.Predecessors > 0)) {
        m.PredecessorIds.IntersectWith(set);
      }
      if (change) _scheduler?.Schedule();
    }


    public void MoveFinished(int moveId, int craneId, TimeStamp started, (int, int, int) hoistDistances, CraneMoveTermination result) {
      var index = World.CraneMoves.Moves.Select((mv, idx) => (mv, idx)).Single(x => x.mv.Id == moveId).idx;
      if (World.CraneMoves.Moves[index].Id != _moveEvents[index].Id) throw new InvalidOperationException("Moves not in sync.");
      _moveEvents.RemoveAt(index);
      World.CraneMoves.Moves.RemoveAt(index);
      _zoneControl.MoveUpdate();
      _scheduler?.Schedule();
    }

    private bool _worldChanged;
    public event EventHandler WorldChanged;
    private void OnWorldChanged(bool kpichange = false) {
      _worldChanged = true;
    }
    private IEnumerable<Event> WorldUpdates() {
      while (true) {
        //if (_worldChanged) {
        GetEstimatedWorldState();

        WorldChanged?.Invoke(this, EventArgs.Empty);
        _worldChanged = false;
        //}
        yield return sim.Timeout(TimeSpan.FromSeconds(1));
      }
    }

    public int HeightBetween(double girderPosition, double targetPosition) {
      var max = 0;
      foreach (var loc in _locations.Where(x => x.GirderPosition >= Math.Min(girderPosition, targetPosition) && x.GirderPosition <= Math.Max(girderPosition, targetPosition))) {
        if (loc.Height > max) max = loc.Height;
      }
      return max;
    }
  }
}
