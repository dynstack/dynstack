using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {
  public interface ICraneScheduleStore {
    void NotifyScheduleChanged(ICraneSchedule schedule);
    CraneScheduleStoreGet Get(ICraneAgent agent, IStackingEnvironment world);
    void Cancel(CraneScheduleStoreGet order);
    Event WhenAny();
    Event WhenEmpty();
    Event WhenChange();
    void AssignMove(int moveId, int craneId, int priority);
    bool HasMovesWaiting(ICraneAgent agent);
    bool IsAssigned(ICraneMoveEvent move);
  }

  public class CraneScheduleStoreGet : Event {
    public DateTime Time { get; private set; }
    public Process Owner { get; set; }
    public ICraneAgent Agent { get; private set; }
    public IStackingEnvironment World { get; }
    public ICraneMoveEvent Move { get; set; }
    public int Priority { get; set; }

    public CraneScheduleStoreGet(SimSharp.Simulation environment, ICraneAgent agent, IStackingEnvironment world)
      : base(environment) {
      Time = environment.Now;
      Owner = environment.ActiveProcess;
      Agent = agent;
      World = world;
    }
  }

  public class CraneScheduleStore : ICraneScheduleStore {
    protected static readonly Func<object, bool> TrueFunc = _ => true;

    protected IStackingEnvironment Environment { get; private set; }
    protected ICraneSchedule Schedule { get; private set; }

    protected LinkedList<CraneScheduleStoreGet> GetQueue { get; private set; }
    protected LinkedList<CraneScheduleStoreGet> ExecutingQueue { get; private set; }

    protected List<Event> WhenAnyQueue { get; private set; }
    protected List<Event> WhenEmptyQueue { get; private set; }
    protected List<Event> WhenChangeQueue { get; private set; }

    protected Dictionary<int, Dictionary<int, bool>> Conflicts { get; set; }

    public CraneScheduleStore(IStackingEnvironment environment, ICraneSchedule schedule) {
      Environment = environment;
      Schedule = schedule;
      GetQueue = new LinkedList<CraneScheduleStoreGet>();
      ExecutingQueue = new LinkedList<CraneScheduleStoreGet>();
      WhenAnyQueue = new List<Event>();
      WhenEmptyQueue = new List<Event>();
      WhenChangeQueue = new List<Event>();

      UpdateConflicts();
    }

    public virtual void NotifyScheduleChanged(ICraneSchedule schedule) {
      var moves = Environment.CraneMoves.ToDictionary(x => x.Id);
      var scheduleByMoveId = schedule.TaskSequence.ToDictionary(x => x.moveId);
      foreach (var id in scheduleByMoveId.Keys.Except(moves.Keys))
        schedule.Remove(id); // move doesn't exist anymore
      foreach (var exe in ExecutingQueue) {
        if (scheduleByMoveId.TryGetValue(exe.Move.Id, out var task)) {
          if (task.craneId != exe.Agent.Id) {
            // the schedule has a different crane assigned to it than is actually executing the move right now
            schedule.UpdateCrane(task.moveId, exe.Agent.Id);
            schedule.UpdateState(task.moveId, CraneScheduleActivityState.Active);
          }
        } else {
          // currently executing move is not part of schedule
          schedule.Insert(0, exe.Move.Id, exe.Agent.Id, 0, CraneScheduleActivityState.Active);
        }
      }
      Schedule = schedule;
      UpdateStates();
      UpdateConflicts();
      TriggerWhenChange();
      TriggerGet();
      TriggerWhenAny();
    }

    public virtual void AssignMove(int moveId, int craneId, int priority) {
      Schedule.Add(moveId, craneId, priority);
      UpdateStates();
      UpdateConflicts();
      TriggerWhenChange();
      TriggerGet();
      TriggerWhenAny();
    }

    public virtual bool HasMovesWaiting(ICraneAgent agent) {
      return Schedule.TaskSequence.Any(x => x.craneId == agent.Id);
    }

    public virtual bool IsAssigned(ICraneMoveEvent move) {
      return Schedule.ContainsMove(move.Id);
    }

    public virtual CraneScheduleStoreGet Get(ICraneAgent agent, IStackingEnvironment world) {
      var get = new CraneScheduleStoreGet(Environment.Environment, agent, world);
      GetQueue.AddLast(get);
      TriggerGet();
      return get;
    }

    public virtual void Cancel(CraneScheduleStoreGet order) {
      ExecutingQueue.Remove(order);

      GetQueue.Remove(order);
      TriggerGet();
    }

    public virtual Event WhenAny() {
      var whenAny = new Event(Environment.Environment);
      WhenAnyQueue.Add(whenAny);
      TriggerWhenAny();
      return whenAny;
    }

    public virtual Event WhenEmpty() {
      var whenEmpty = new Event(Environment.Environment);
      WhenEmptyQueue.Add(whenEmpty);
      TriggerWhenEmpty();
      return whenEmpty;
    }

    public virtual Event WhenChange() {
      var whenChange = new Event(Environment.Environment);
      WhenChangeQueue.Add(whenChange);
      return whenChange;
    }

    protected long lastPrio = long.MinValue;

    protected virtual void DoGet(CraneScheduleStoreGet get) {
      if (Schedule.Tasks == 0) return;
      var topPrio = Schedule.TaskSequence.Min(x => x.priority);
      foreach (var task in Schedule.TaskSequence.Where(x => x.priority == topPrio)) {
        // TODO: account of deadlocks (cycles in conflict graph)
        if (task.craneId == get.Agent.Id && !Schedule.TaskSequence.Any(x => Conflicts[task.moveId][x.moveId])) {
          get.Move = Environment.CraneMoves.Single(x => x.Id == task.moveId);
          lastPrio = Math.Max(Environment.Now.MilliSeconds, lastPrio + 1);
          get.Priority = (int)(lastPrio % 3_600_000);
          ExecutingQueue.AddLast(get);
          Environment.Environment.Process(RemoveFromSchedule(get));
          UpdateStates();
          UpdateConflicts();
          get.Succeed();
          return;
        }
      }
    }

    protected IEnumerable<Event> RemoveFromSchedule(CraneScheduleStoreGet get) {
      yield return get.Move.Started;
      if (Environment.Environment.ActiveProcess.HandleFault()) yield break;
      var started = Environment.Now;
      var pickupLoc = get.World.LocationResources.Single(x => x.Id == get.Move.PickupLocation);
      var pickupHoistDownDistance = (int)get.Agent.HoistLevel - pickupLoc.Height;
      var pickupHoistUpDistance = get.World.Height - get.Agent.Capacity - pickupLoc.Height;
      yield return get.Move.Finished;
      if (!get.Move.Finished.IsOk) yield break;
      var dropoffLoc = get.World.LocationResources.Single(x => x.Id == get.Move.DropoffLocation);
      var dropoffHoistDownDistance = get.World.Height - get.Agent.Capacity - (dropoffLoc.Height - 1);
      Schedule.Remove(get.Move.Id);
      ExecutingQueue.Remove(get);
      UpdateStates();
      UpdateConflicts();
      Environment.MoveFinished(get.Move.Id, get.Agent.Id, started, (pickupHoistDownDistance, pickupHoistUpDistance, dropoffHoistDownDistance), get.Move.Finished.IsOk ? CraneMoveTermination.Success : CraneMoveTermination.Invalid);
      foreach (var m in Environment.CraneMoves)
        m.RemoveFromPredecessors(get.Move.Id);
      TriggerWhenChange();
      if (Schedule.Tasks == 0) TriggerWhenEmpty();
      else TriggerGet();
      Environment.Environment.ActiveProcess.HandleFault();
    }

    protected void UpdateStates() {
      if (!Schedule.TaskSequence.Any()) {
        return;
      }
      var topPrio = Schedule.TaskSequence.Min(x => x.priority);
      foreach (var task in Schedule.TaskSequence.OrderBy(x => x.priority).ThenBy(x => x.index)) {
        var move = Environment.CraneMoves.SingleOrDefault(x => x.Id == task.moveId);
        if (move == null) continue;
        var state = CraneScheduleActivityState.Created;
        if (ExecutingQueue.Any(x => x.Move.Id == task.moveId)) // a crane is currently executing the move
          state = CraneScheduleActivityState.Active;
        else if (topPrio == task.priority && move.Predecessors == 0) // the move has minimum priority and there are no predecessors
          state = CraneScheduleActivityState.Activatable;
        Schedule.UpdateState(task.moveId, state);
      }
    }

    protected void UpdateConflicts() {
      var moves = Environment.CraneMoves.ToDictionary(x => x.Id);
      var cranes = Environment.CraneAgents.ToDictionary(x => x.Id);
      var sequence = Schedule.TaskSequence.ToList();

      Conflicts = new Dictionary<int, Dictionary<int, bool>>();

      foreach (var s1 in sequence) {
        if (!moves.TryGetValue(s1.moveId, out var m1)) { // move m1 does not exist anymore
          Conflicts[s1.moveId] = sequence.ToDictionary(x => x.moveId, x => false);
          continue;
        }

        var c1 = cranes[s1.craneId];
        var minPos1 = Math.Min(m1.PickupGirderPosition, m1.DropoffGirderPosition);
        var maxPos1 = Math.Max(m1.PickupGirderPosition, m1.DropoffGirderPosition);

        Conflicts[s1.moveId] = new Dictionary<int, bool>();

        foreach (var s2 in sequence) {
          if (s1.moveId == s2.moveId) {
            Conflicts[s1.moveId][s2.moveId] = false; // no self-conflict
            continue;
          }

          if (s1.priority != s2.priority) {
            Conflicts[s1.moveId][s2.moveId] = s1.priority > s2.priority; // conflict by design
            continue;
          }

          if (!moves.TryGetValue(s2.moveId, out var m2)) { // move m2 does not exist anymore
            Conflicts[s1.moveId][s2.moveId] = false;
            continue;
          }
          var c2 = cranes[s2.craneId];

          var minPos2 = Math.Min(m2.PickupGirderPosition, m2.DropoffGirderPosition);
          var maxPos2 = Math.Max(m2.PickupGirderPosition, m2.DropoffGirderPosition);

          // same crane, but s1 has a higher index
          var conflict0 = s1.craneId == s2.craneId && s1.index > s2.index;

          // s2 is a predecessor of s1
          var conflict1 = m1.PredecessorIds.Contains(m2.Id);

          // s1 overlaps s2, but could be executed if s2 has already started
          var conflict2 = (
                 (c1.GetGirderPosition() < c2.GetGirderPosition()
                  && m1.PickupGirderPosition + (c1.Width + c2.Width) / 2.0 < m2.PickupGirderPosition
                  && m1.DropoffGirderPosition + (c1.Width + c2.Width) / 2.0 < m2.DropoffGirderPosition
                  && m1.DropoffGirderPosition > m2.PickupGirderPosition)
              || (c1.GetGirderPosition() > c2.GetGirderPosition()
                  && m1.PickupGirderPosition - (c1.Width + c2.Width) / 2.0 > m2.PickupGirderPosition
                  && m1.DropoffGirderPosition - (c1.Width + c2.Width) / 2.0 > m2.DropoffGirderPosition
                  && m1.DropoffGirderPosition < m2.PickupGirderPosition)
          );
          conflict2 &= ExecutingQueue.All(x => x.Move.Id != s2.moveId);

          // s1 overlaps s2
          var conflict3 = !conflict2 && (
                 (c1.GetGirderPosition() < c2.GetGirderPosition()
                  && (maxPos1 + (c1.Width + c2.Width) / 2.0 > minPos2
                      || minPos1 + (c1.Width + c2.Width) / 2.0 > maxPos2))
              || (c1.GetGirderPosition() > c2.GetGirderPosition()
                  && (minPos1 - (c1.Width + c2.Width) / 2.0 < maxPos2
                      || maxPos1 - (c1.Width + c2.Width) / 2.0 < minPos2))
          );

          Conflicts[s1.moveId][s2.moveId] = conflict0 || conflict1 || conflict2 || conflict3;
        }
      }
    }

    protected virtual void TriggerGet(Event @event = null) {
      var current = GetQueue.First;
      while (current != null) {
        var get = current.Value;
        DoGet(get);
        if (get.IsTriggered) {
          var next = current.Next;
          GetQueue.Remove(current);
          current = next;
          TriggerWhenEmpty();
          TriggerWhenChange();
        } else current = current.Next;
        if (Schedule.Tasks == 0) break;
      }
    }

    protected virtual void TriggerWhenAny() {
      if (Schedule.Tasks > 0) {
        if (WhenAnyQueue.Count == 0) return;
        foreach (var evt in WhenAnyQueue)
          evt.Succeed();
        WhenAnyQueue.Clear();
      }
    }

    protected virtual void TriggerWhenEmpty() {
      if (Schedule.Tasks == 0) {
        if (WhenEmptyQueue.Count == 0) return;
        foreach (var evt in WhenEmptyQueue)
          evt.Succeed();
        WhenEmptyQueue.Clear();
      }
    }

    protected virtual void TriggerWhenChange() {
      if (WhenChangeQueue.Count == 0) return;
      foreach (var evt in WhenChangeQueue)
        evt.Succeed();
      WhenChangeQueue.Clear();
    }
  }
}
