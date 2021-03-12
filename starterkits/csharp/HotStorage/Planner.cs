using System;
using System.Linq;
using System.Collections.Generic;
using DynStacking.HotStorage.DataModel;
using Google.Protobuf;

namespace DynStacking.HotStorage {

  public class Planner : IPlanner {
    public byte[] PlanMoves(byte[] worldData, OptimizerType opt) {
      return PlanMoves(World.Parser.ParseFrom(worldData), opt)?.ToByteArray();
    }

    private CraneSchedule PlanMoves(World world, OptimizerType opt) {
      if (world.Crane.Schedule.Moves.Count > 0) {
        return null;
      }
      var schedule = new CraneSchedule();
      schedule.SequenceNr = 1;
      switch (opt) {
        case OptimizerType.RuleBased: MakeHeuristicSchedule(world, schedule); break;
        case OptimizerType.ModelBased: new ModelBasedOptimizer(world).CalculateSchedule(schedule); break;
      }
      if (schedule.Moves.Count > 0) {
        schedule.SequenceNr = world.Crane.Schedule.SequenceNr;
        return schedule;
      } else {
        return null;
      }
    }

    public static void MakeHeuristicSchedule(World world, CraneSchedule schedule) {
      AnyHandoverMove(world, schedule);
      ClearProductionStack(world, schedule);
    }

    /// If any block on top of a stack can be moved to the handover schedule this move.
    private static void AnyHandoverMove(World world, CraneSchedule schedule) {
      if (!world.Handover.Ready) {
        return;
      }
      foreach (var stack in world.Buffers) {
        var blocks = stack.BottomToTop.Count;
        if (blocks > 0) {
          var top = stack.BottomToTop[blocks - 1];
          if (top.Ready) {
            var move = new CraneMove();
            move.BlockId = top.Id;
            move.SourceId = stack.Id;
            move.TargetId = world.Handover.Id;
            schedule.Moves.Add(move);
          }
        }
      }

    }

    /// If the top block of the production stack can be put on a buffer schedule this move.
    private static void ClearProductionStack(World world, CraneSchedule schedule) {
      var blocks = world.Production.BottomToTop.Count;
      if (blocks > 0) {
        var top = world.Production.BottomToTop[blocks - 1];
        foreach (var stack in world.Buffers) {
          if (stack.MaxHeight > stack.BottomToTop.Count) {
            var move = new CraneMove();
            move.BlockId = top.Id;
            move.SourceId = world.Production.Id;
            move.TargetId = stack.Id;
            schedule.Moves.Add(move);
            return;
          }
        }
      }
    }
  }
}