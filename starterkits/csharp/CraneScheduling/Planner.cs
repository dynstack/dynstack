﻿using DynStacking.CraneScheduling.DataModel;
using Google.Protobuf;
using System;
using System.Linq;

namespace DynStacking.CraneScheduling {
  internal class Planner : IPlanner {
    private int scheduleNr;

    public byte[] PlanMoves(byte[] worldData, OptimizerType opt) {
      return PlanMoves(World.Parser.ParseFrom(worldData), opt)?.ToByteArray();
    }

    private CraneSchedulingSolution PlanMoves(World world, OptimizerType _) {
      if (!world.CraneMoves.Any()) return null;

      var schedule = new CraneSchedule {
        ScheduleNr = ++scheduleNr // only for debugging
      };

      // create schedule with moves generated by simulation
      foreach (var item in world.CraneMoves) {
        var craneId = 1 + Math.Abs(item.Id) % 2;
        var crane = world.Cranes[craneId - 1];

        // fix crane assignment if necessary
        if (!crane.CanReach(item.PickupGirderPosition) || !crane.CanReach(item.DropoffGirderPosition)) {
          craneId = craneId % 2 + 1;
        }

        var activity = new CraneScheduleActivity {
          CraneId = craneId,
          MoveId = item.Id
        };

        schedule.Activities.Add(activity);
      }

      var solution = new CraneSchedulingSolution {
        Schedule = schedule
      };

      // custom moves could be added here
      // solution.CustomMoves.AddRange(...)

      return solution;
    }
  }
}
