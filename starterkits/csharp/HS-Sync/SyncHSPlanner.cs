using DynStacking;
using DynStacking.HotStorage.DataModel;
using Google.Protobuf;
using Google.Protobuf.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace csharp.HS_Sync {
  public class SyncHSPlanner : IPlanner {
    private int seqNr = 0;

    public byte[] PlanMoves(byte[] worldData, OptimizerType opt) {
      return PlanMoves(World.Parser.ParseFrom(worldData), opt)?.ToByteArray();
    }

    private CraneSchedule PlanMoves(World world, OptimizerType opt) {
      if (world.Buffers == null || (world.Crane.Schedule.Moves?.Count ?? 0) > 0) {
        if (world.Buffers == null)
          Console.WriteLine($"Cannot calculate, incomplete world.");
        else
          Console.WriteLine($"Crane already has {world.Crane.Schedule.Moves?.Count} moves");
        return null;
      }

      var schedule = new CraneSchedule() { SequenceNr = seqNr++ };
      var initial = new RFState(world);
      var solution = initial.GetBestMovesBeam(new List<CraneMove>(), 6, 5);
      var list = solution.Item1.ConsolidateMoves();
      if (solution != null)
        schedule.Moves.AddRange(list.Take(3)
                                .TakeWhile(move => world.Handover.Ready || move.TargetId != world.Handover.Id));

      if (schedule.Moves.Count > 0) {
        Console.WriteLine($"Delivering answer for Worldtime {world.Now}");
        return schedule;
      } else {
        Console.WriteLine($"No answer for Worldtime {world.Now}");
        return null;
      }
    }
  }
}
