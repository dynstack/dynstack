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

namespace DynStack.Simulation.CS {
  public class CraneSchedulingSimulation : IStackingEnvironment {
    private World _world;
    public SimSharp.ThreadSafeSimulation Environment { get; private set; }
    SimSharp.Simulation IStackingEnvironment.Environment => Environment;
    public TimeStamp Now => Environment.NowTS();

    public int Height => _world.Height;

    public double Width => _world.Width;

    private List<ILocationResource> _locations;
    public IEnumerable<ILocationResource> LocationResources => _locations;
    public IEnumerable<IBlock> Blocks => _world.AllBlocks();
    private List<ICraneAgent> _agents;
    public IEnumerable<ICraneAgent> CraneAgents => _agents;
    private List<ICraneMoveEvent> _moves;
    public IEnumerable<ICraneMoveEvent> CraneMoves => _moves;
    public IEnumerable<IMoveRequest> MoveRequests => _world.MoveRequests;
    private ICraneScheduleStore _scheduleStore;
    public ICraneScheduleStore CraneScheduleStore => _scheduleStore;
    private IZoneControl _zoneControl;
    public IZoneControl ZoneControl => _zoneControl;

    private ICraneScheduler _scheduler;

    public CraneSchedulingSimulation(World world) {
      Environment = new PseudoRealtimeSimulation(42);
      ((PseudoRealtimeSimulation)Environment).SetRealtime();
      _world = world;

      _locations = new List<ILocationResource>();
      _agents = new List<ICraneAgent>();
      _moves = new List<ICraneMoveEvent>();
    }
    // TODO: hen-egg problem, BasicScheduler needs instance of IStackingEnvironment
    public CraneSchedulingSimulation(World world, ICraneScheduler scheduler) : this(world) {
      ((PseudoRealtimeSimulation)Environment).SetVirtualtime();
      _scheduler = scheduler;
    }
    public void SetLogger(TextWriter logger) {
      Environment.Logger = logger;
    }

    private void Initialize() {
      var blockId = 1;
      foreach (var loc in _world.Locations) {
        loc.Stack.BottomToTop.Clear();
        loc.Stack.BottomToTop.Add(new Block() { Id = blockId });
        blockId++;
      }
      foreach (var loc in _world.Locations) {
        _locations.Add(new LocationResource(this, loc));
      }
      foreach (var crane in _world.Cranes) {
        _agents.Add(new CraneAgent(this, crane,
          girderSpeed: new TriangularDistribution(Environment, 1, 3, 2),
          hoistSpeed: new TriangularDistribution(Environment, 1, 3, 2),
          manipulationTime: new TriangularDistribution(Environment, 5, 10, 7.5)));
      }
      foreach (var move in _world.CraneMoves) {
        _moves.Add(new CraneMoveEvent(Environment, move));
      }
      _scheduleStore = new CraneScheduleStore(this, _world.CraneSchedule);
      _zoneControl = new ZoneControl(this);

      Environment.Process(OrderGenerator());
      Environment.Process(SpaceTimeDrawing());
      Environment.Process(LockArea());
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

    public async Task<World> RunAsync(TimeSpan duration) {
      Initialize();
      await Environment.RunAsync(duration);
      return _world;
    }

    public void StopAsync() => Environment.StopAsync();

    public Task SetCraneScheduleAsync(ICraneSchedule schedule) {
      throw new NotImplementedException();
    }

    private IEnumerable<Event> OrderGenerator() {
      var moveId = _world.CraneMoves.Count == 0 ? 0 : _world.CraneMoves.Select(x => x.Id).Max();
      while (true) {
        var newMoves = false;
        foreach (var b in _world.AllBlocks()) {
          var loc = _world.Locations.SingleOrDefault(x => x.Stack.Topmost == b);
          if (loc == null || _world.CraneMoves.Any(x => x.PickupLocationId == loc.Id || x.DropoffLocationId == loc.Id)) continue;
          var freeTargets = (from l in _world.Locations
                             let lm = _world.CraneMoves.Where(x => x.Type == MoveType.PickupAndDropoff
                                && (x.PickupLocationId == l.Id || x.DropoffLocationId == l.Id)).ToList()
                             where l.Id != loc.Id
                               && lm.Sum(x => {
                                 if (x.PickupLocationId == l.Id) return -x.Amount;
                                 if (x.DropoffLocationId == l.Id) return x.Amount;
                                 return 0;
                               }) + l.Height < l.MaxHeight
                               && l.FreeHeight > 0
                             select new { Location = l, Moves = lm }).ToList();
          if (freeTargets.Count == 0) continue;

          var choice = Environment.RandChoice(freeTargets, freeTargets.Select(x => 1.0).ToList());
          var move = new CraneMove() {
            Amount = 1,
            DueDate = new TimeStamp(long.MaxValue),
            PickupLocationId = loc.Id,
            PickupGirderPosition = loc.GirderPosition,
            DropoffLocationId = choice.Location.Id,
            DropoffGirderPosition = choice.Location.GirderPosition,
            Id = ++moveId,
            PredecessorIds = new HashSet<int>(choice.Moves.Select(x => x.Id)),
            ReleaseTime = Now,
            Type = MoveType.PickupAndDropoff
          };
          _world.CraneMoves.Add(move);
          _moves.Add(new CraneMoveEvent(Environment, move));
          newMoves = true;
        }
        if (newMoves) _scheduler.Schedule();
        yield return new AnyOf(Environment, CraneMoves.Select(x => x.Finished));
        foreach (var finished in CraneMoves.Where(x => x.Finished.IsProcessed).ToList()) {
          for (var i = 0; i < _world.CraneMoves.Count; i++) if (_world.CraneMoves[i].Id == finished.Id) { _world.CraneMoves.RemoveAt(i); break; }
          _moves.Remove(finished);
        }
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
        _lock = (Math.Min(a, b) * _world.Width, Math.Max(a, b) * _world.Width);
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
        var lockStart = (int)Math.Floor(_lock.lower / _world.Width * 100);
        var lockEndPrinted = !_locked;
        var lockEnd = (int)Math.Floor(_lock.upper / _world.Width * 100);
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
          var pos = (int)Math.Floor(m.GetGirderPosition() / _world.Width * 100);
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
      if (_world == null) return _world;

      _world.Now = Now;

      foreach (var agent in CraneAgents) {
        agent.GetGirderPosition(); // will cause an update
      }

      return _world;
    }

    public void MoveFinished(int id, bool success) {
      _moves.RemoveAll(x => x.Id == id);
      _world.CraneMoves.RemoveAll(x => x.Id == id);
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
