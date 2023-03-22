using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using DynStack.DataModel.CS;
using TLRP = DynStack.Simulation.CS.Policy.TargetLocationRecommendation;

namespace DynStack.Simulation.CS.Policy.MoveCreation {
  public class DefaultPolicy : IPolicy {
    private int moveId;
    private readonly TLRP.IPolicy tlrPolicy;

    public DefaultPolicy(int seed) {
      tlrPolicy = new TLRP.RandomPolicy(seed);
    }

    public IEnumerable<CraneMove> GetMoves(Block block, World world) {
      if (world.CraneMoves.Any(x => x.MovedBlockIds.Contains(block.Id))) {
        return Enumerable.Empty<CraneMove>();
      }

      var moveReq = world.MoveRequests.Single(x => x.BlockId == block.Id);
      var srcLoc = world.Locations.SingleOrDefault(x => x.Stack.BottomToTop.Contains(block));
      var tgtLoc = world.Locations.SingleOrDefault(x => x.Id == moveReq.TargetLocationId);
      var moveBlockIds = new List<int> { block.Id };

      var predecessors = world.CraneMoves.Where(x => x.PickupLocationId == srcLoc.Id).ToList();

      var moves = new List<CraneMove>();

      if (srcLoc.Type == StackTypes.Buffer) { // only relocate blocks above for buffer locations
        var storedAbove = srcLoc.Stack.TopToBottom.TakeWhile(x => x != block);

        foreach (var b in storedAbove) {
          var existingRelocation = predecessors.SingleOrDefault(x => x.MovedBlockIds.Contains(b.Id));

          if (existingRelocation == null) {
            var relTgtLoc = tlrPolicy.GetLocation(b, world);
            var relBlockIds = new List<int> { b.Id };

            var relocation = new CraneMove {
              Id = --moveId,
              Type = MoveType.PickupAndDropoff,
              PickupLocationId = srcLoc.Id,
              PickupGirderPosition = srcLoc.GirderPosition,
              DropoffLocationId = relTgtLoc.Id,
              DropoffGirderPosition = relTgtLoc.GirderPosition,
              Amount = relBlockIds.Count,
              ReleaseTime = world.Now,
              DueDate = moveReq.DueDate,
              PredecessorIds = new HashSet<int>(moves.Select(x => x.Id)),
              MovedBlockIds = moveBlockIds
            };

            moves.Add(relocation);
          } else {
            moves.Add(existingRelocation);
          }
        }

        predecessors.AddRange(moves);
      }

      var move = new CraneMove {
        Id = --moveId,
        Type = MoveType.PickupAndDropoff,
        PickupLocationId = srcLoc.Id,
        PickupGirderPosition = srcLoc.GirderPosition,
        DropoffLocationId = tgtLoc.Id,
        DropoffGirderPosition = tgtLoc.GirderPosition,
        Amount = moveBlockIds.Count,
        ReleaseTime = world.Now,
        DueDate = new TimeStamp((long)Math.Floor(TimeSpan.MaxValue.TotalMilliseconds)),
        PredecessorIds = new HashSet<int>(predecessors.Select(x => x.Id)),
        MovedBlockIds = moveBlockIds
      };

      moves.Add(move);
      return moves;
    }
  }
}
