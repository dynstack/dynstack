using System;
using System.Linq;
using System.Collections.Generic;
using DynStacking.RollingMill.DataModel;
using Google.Protobuf;

namespace DynStacking.RollingMill {
  class Planner : IPlanner {
    World world;
    PlannedCraneMoves plan;

    public byte[] PlanMoves(byte[] worldData, OptimizerType opt) {
      world = World.Parser.ParseFrom(worldData);
      PlanMovesHeuristic();
      return plan?.ToByteArray();
    }

    private void PlanMovesHeuristic() {
      plan = new PlannedCraneMoves();
      // in the rolling mill we got to cranes we can plan independendly.
      if (!world.CraneMoves.Moves.Any(mov => mov.RequiredCraneId == world.HandoverCrane.Id)) {
        PlanHandoverCrane();
      }
      if (!world.CraneMoves.Moves.Any(mov => mov.RequiredCraneId == world.ShuffleCrane.Id)) {
        PlanShuffleCrane();
      }
    }




    IEnumerable<Location> ArrivalStacks() {
      foreach (var loc in world.Locations) {
        if (loc.Type == DynStacking.RollingMill.DataModel.StackTypes.ArrivalStack) {
          yield return loc;
        }
      }
    }

    IEnumerable<Location> BufferStacks() {
      foreach (var loc in world.Locations) {
        if (loc.Type == DynStacking.RollingMill.DataModel.StackTypes.ShuffleBuffer || loc.Type == DynStacking.RollingMill.DataModel.StackTypes.SortedBuffer) {
          yield return loc;
        }
      }
    }

    int RemainingCapacity(Location location) => location.MaxHeight - SizeOf(location);

    int SizeOf(Location location) => location.Stack.BottomToTop.Count;


    void PlanHandoverCrane() {
      var moveId = plan.Moves.Count;
      var sourceRequest = new List<(Location, int, Block, MoveRequest)>();
      foreach (var req in world.MoveRequests) {
        foreach (var src in BufferStacks()) {
          var (block, pos) = src.Stack.BottomToTop.Select((slab, idx) => (slab, idx)).FirstOrDefault(x => x.slab.Id == req.BlockId);
          if (block != null) {
            sourceRequest.Add((src, pos, block, req));
            break;
          }
        }
      }


      sourceRequest.Sort((a, b) => (SizeOf(a.Item1) - a.Item2).CompareTo(SizeOf(a.Item1) - a.Item2));

      foreach (var (src, pos, block, req) in sourceRequest) {
        var ty = block.Type;
        var seq = block.Sequence;
        var couldTakeTopN = src.Stack.BottomToTop.Reverse().TakeWhile(b => {
          var isNext = b.Type == ty && b.Sequence == seq;
          seq += 1;
          return isNext;
        }).Count();

        var mov = new CraneMove();
        moveId += 1;
        mov.Id = moveId;
        mov.Type = MoveType.PickupAndDropoff;
        mov.ReleaseTime = new TimeStamp() { MilliSeconds = world.Now.MilliSeconds };
        mov.PickupLocationId = src.Id;

        if (couldTakeTopN > 0) {
          var amount = Math.Min(couldTakeTopN, world.HandoverCrane.CraneCapacity);
          mov.DropoffLocationId = req.TargetLocationId;
          mov.RequiredCraneId = world.HandoverCrane.Id;
          mov.Amount = amount;
        } else {
          // Relocate blocks that are in the way
          var mustRelocate = SizeOf(src) - pos - 1;
          var amount = Math.Min(mustRelocate, world.HandoverCrane.CraneCapacity);
          var tgt = BufferStacks().FirstOrDefault(tgt => tgt.Id != src.Id && RemainingCapacity(tgt) >= amount);
          if (tgt != null) {
            mov.DropoffLocationId = tgt.Id;
            mov.RequiredCraneId = world.HandoverCrane.Id;
            mov.Amount = amount;
          } else {
            continue;
          }
        }
        plan.Moves.Add(mov);
        return;
      }
    }

    void PlanShuffleCrane() {
      var dontUse = new List<int>();
      foreach (var mov in plan.Moves) {
        dontUse.Add(mov.PickupLocationId);
        dontUse.Add(mov.DropoffLocationId);
      }
      var move_id = plan.Moves.Count;
      var src = ArrivalStacks().OrderBy(loc => loc.Stack.BottomToTop.Any() ? loc.Stack.BottomToTop.Min(block => block.Sequence) : int.MaxValue).First();

      var amount = Math.Min(SizeOf(src), world.ShuffleCrane.CraneCapacity);
      if (amount == 0) {
        return;
      }
      var tgt = BufferStacks().FirstOrDefault(tgt => RemainingCapacity(tgt) >= amount && !dontUse.Contains(tgt.Id));
      if (tgt != null) {
        var mov = new CraneMove();
        move_id += 1;
        mov.Id = move_id;
        mov.Type = MoveType.PickupAndDropoff;
        mov.ReleaseTime = new TimeStamp() { MilliSeconds = world.Now.MilliSeconds };
        mov.PickupLocationId = src.Id;
        mov.DropoffLocationId = tgt.Id;
        mov.RequiredCraneId = world.ShuffleCrane.Id;
        mov.Amount = amount;
        plan.Moves.Add(mov);
        return;
      }
    }

  }
}