using System.Linq;
using System.Collections.Generic;
using DynStacking.HotStorage.DataModel;

namespace DynStacking.HotStorage {
  public class ModelBasedOptimizer {
    World world;
    public ModelBasedOptimizer(World world) {
      this.world = world;
    }

    /// Generate a schedule for the current world state by solving an restricted offline BRP
    public void CalculateSchedule(CraneSchedule schedule) {
      var priorities = PrioritizeByDueDate();
      var initalState = new BrpState(world, priorities);
      var solution = DepthFirstSearch(initalState);
      FillScheduleFromSolution(solution, schedule);
    }

    /// Assign a priority to each block based on its due date.
    /// This is not a good strategy but it is very simple.
    /// The block with the lowest priority (due date) has to be retrieved first.
    public Dictionary<int, int> PrioritizeByDueDate() {
      return world.Production.BottomToTop
          .Concat(world.Buffers
              .SelectMany(stack => stack.BottomToTop))
          .OrderBy(block => block.Due.MilliSeconds)
          .Select((block, Prio) => new { block.Id, Prio })
          .ToDictionary(x => x.Id, x => x.Prio);
    }

    /// Does a simple depth first search using forced moves starting from the initial BrpState.
    public List<CraneMove> DepthFirstSearch(BrpState initial) {
      var budget = 10000;
      List<CraneMove> best = null;
      var stack = new Stack<BrpState>();
      stack.Push(initial);

      while (stack.Count > 0 && budget > 0) {
        budget -= 1;
        var state = stack.Pop();
        if (state.IsSolved) {
          if (best == null || best.Count > state.Moves.Count) {
            best = state.Moves;
          }
        } else {
          foreach (var move in state.ForcedMoves()) {
            stack.Push(state.Apply(move));
          }
        }
      }

      return best;
    }

    /// Translates the BRP solution into a CraneSchedule
    public void FillScheduleFromSolution(List<CraneMove> solution, CraneSchedule schedule) {
      var handover = world.Handover;
      schedule.Moves.AddRange(solution
          .Take(3)
          .TakeWhile(move => handover.Ready || move.TargetId != handover.Id)
      );
    }
  }
}