using System;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation.CS {
  public class CSCraneScheduleStore : CraneScheduleStore {
    public CSCraneScheduleStore(
      CraneSchedulingSimulation environment,
      ICraneSchedule schedule
    ) : base(
      environment,
      schedule
    ) { }

    protected override void TriggerGet(Event @event = null) {
      var current = GetQueue.First;
      while (current != null) {
        var get = current.Value;
        DoGet(get);
        if (get.IsTriggered) {
          GetQueue.Remove(current);
          TriggerWhenEmpty();
          TriggerWhenChange();
          break;
        } else current = current.Next;
        if (Schedule.Tasks == 0) break;
      }
    }

    protected override void DoGet(CraneScheduleStoreGet get) {
      if (Schedule.Tasks == 0) return;

      var available = Schedule.TaskSequence
        .SkipWhile(x => x.state == CraneScheduleActivityState.Active)
        .ToArray();

      if (!available.Any()) return;

      var next = available.First();

      if (next.craneId == get.Agent.Id) {
        var move = Environment.CraneMoves.Single(x => x.Id == next.moveId);
        var executingMoveIds = Environment.CraneAgents.Cast<CSCraneAgent>()
                                                      .Where(x => x.CurrentMove != null)
                                                      .Select(x => x.CurrentMove.Id);
        if (move.PredecessorIds.Except(executingMoveIds).Any()) return;

        if (move.Type == MoveType.PickupAndDropoff) {
          var block = move.MovedBlocks.Single();
          var srcLoc = Environment.LocationResources.Single(x => x.Id == move.PickupLocation);
          if (srcLoc.Topmost?.Id != block) return;
        }

        get.Move = move;
        lastPrio = Math.Max(Environment.Now.MilliSeconds, lastPrio + 1);
        get.Priority = (int)(lastPrio % 3_600_000);

        ExecutingQueue.AddLast(get);
        Environment.Environment.Process(RemoveFromSchedule(get));
        UpdateStates();
        get.Succeed();
      }
    }
  }
}
