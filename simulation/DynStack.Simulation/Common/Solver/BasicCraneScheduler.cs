using System;
using System.Collections.Generic;
using System.Linq;
using SimSharp;

namespace DynStack.Simulation {
  public class BasicCraneScheduler : ICraneScheduler {
    private IStackingEnvironment _world;
    private Process _mainProcess;

    public BasicCraneScheduler(IStackingEnvironment world) {
      _world = world;
      _mainProcess = world.Environment.Process(Main());
    }

    public virtual void Schedule() {
      _mainProcess.Interrupt();
    }

    private IEnumerable<Event> Main() {
      while (true) {
        // Assign the next executable move from the list of moves
        foreach (var move in _world.CraneMoves.Where(x => x.Assigned == null && !_world.CraneScheduleStore.IsAssigned(x))) {
          var agent = CraneAssignmentHeuristic(move);
          if (agent == null || _world.CraneScheduleStore.HasMovesWaiting(agent)) continue;
          if (IsExecutable(move, agent)) {
            move.Assigned = agent;
            _world.CraneScheduleStore.AssignMove(move.Id, agent.Id, 0);
            var cur = move;
          } else continue;
        }
        yield return _world.CraneScheduleStore.WhenChange();
        _world.Environment.ActiveProcess.HandleFault();
      }
    }

    private bool IsExecutable(ICraneMoveEvent move, ICraneAgent agent) {
      if (move.Predecessors > 0 || move.ReleaseTime > _world.Now) return false;
      if (!agent.CanReach(move.PickupGirderPosition) || !agent.CanReach(move.DropoffGirderPosition)) return false;
      if (_world.CraneAgents.Any(other => other != agent
        && other.State != CraneAgentState.Waiting
        && ((other.GetGirderPosition() < agent.GetGirderPosition() && (Math.Max(other.GoalPosition1, other.GoalPosition2) + other.Width / 2 > Math.Min(move.PickupGirderPosition, move.DropoffGirderPosition) - agent.Width / 2))
          || (other.GetGirderPosition() > agent.GetGirderPosition() && (Math.Min(other.GoalPosition1, other.GoalPosition2) - other.Width / 2 < Math.Max(move.PickupGirderPosition, move.DropoffGirderPosition) + agent.Width / 2)))))
        return false;
      return true;
    }

    private ICraneAgent CraneAssignmentHeuristic(ICraneMoveEvent order) {
      if (order.RequiredCraneId.HasValue) {
        // there is a required crane assigned to complete this order
        return _world.CraneAgents.Single(x => x.Id == order.RequiredCraneId.Value);
      }

      foreach (var agent in _world.CraneAgents.Where(x => IsExecutable(order, x))
          .OrderBy(x => Math.Abs(x.GetGirderPosition() - order.PickupGirderPosition) + Math.Abs(x.GetGirderPosition() - order.DropoffGirderPosition))) {
        return agent;
      }

      return null;
    }

  }
}
