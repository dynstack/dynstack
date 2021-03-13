using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {
  public interface ICraneScheduleStore {
    void NotifyScheduleChanged();
    CraneScheduleStoreGet Get(ICraneAgent agent);
    void CancelWaiting(CraneScheduleStoreGet order);
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
    public ICraneMoveEvent Move { get; set; }

    public CraneScheduleStoreGet(SimSharp.Simulation environment, ICraneAgent agent)
      : base(environment) {
      Time = environment.Now;
      Owner = environment.ActiveProcess;
      Agent = agent;
    }
  }

  public class CraneScheduleStore : ICraneScheduleStore {
    protected static readonly Func<object, bool> TrueFunc = _ => true;

    protected IStackingEnvironment Environment { get; private set; }
    protected ICraneSchedule Schedule { get; private set; }

    protected LinkedList<CraneScheduleStoreGet> GetQueue { get; private set; }

    protected List<Event> WhenAnyQueue { get; private set; }
    protected List<Event> WhenEmptyQueue { get; private set; }
    protected List<Event> WhenChangeQueue { get; private set; }

    public CraneScheduleStore(IStackingEnvironment environment, ICraneSchedule schedule) {
      Environment = environment;
      Schedule = schedule;
      GetQueue = new LinkedList<CraneScheduleStoreGet>();
      WhenAnyQueue = new List<Event>();
      WhenEmptyQueue = new List<Event>();
      WhenChangeQueue = new List<Event>();
    }

    public virtual void NotifyScheduleChanged() {
      TriggerWhenChange();
      TriggerGet();
      TriggerWhenAny();
    }

    public virtual void AssignMove(int moveId, int craneId, int priority) {
      Schedule.Add(moveId, craneId, priority);
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

    public virtual CraneScheduleStoreGet Get(ICraneAgent agent) {
      var get = new CraneScheduleStoreGet(Environment.Environment, agent);
      GetQueue.AddLast(get);
      TriggerGet();
      return get;
    }

    public virtual void CancelWaiting(CraneScheduleStoreGet order) {
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

    protected virtual void DoGet(CraneScheduleStoreGet get) {
      var topPrio = int.MaxValue;
      foreach (var task in Schedule.TaskSequence) {
        if (topPrio == int.MaxValue) topPrio = task.priority;
        else if (topPrio != task.priority) break;
        if (task.craneId == get.Agent.Id) {
          get.Move = Environment.CraneMoves.Single(x => x.Id == task.moveId);
          Environment.Environment.Process(RemoveFromSchedule(get.Move));
          get.Succeed();
          return;
        }
      }
    }

    private IEnumerable<Event> RemoveFromSchedule(ICraneMoveEvent move) {
      yield return move.Finished;
      Schedule.Remove(move.Id);
      Environment.MoveFinished(move.Id, move.Finished.IsOk);
      foreach (var m in Environment.CraneMoves)
        m.RemoveFromPredecessors(move.Id);
      TriggerWhenChange();
      if (Schedule.Tasks == 0) TriggerWhenEmpty();
      Environment.Environment.ActiveProcess.HandleFault();
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
