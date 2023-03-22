using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using DynStack.DataModel.CS;
using MC = DynStack.Simulation.CS.Policy.MoveCreation;
using TLR = DynStack.Simulation.CS.Policy.TargetLocationRecommendation;

namespace DynStack.Simulation.CS {
  public class MoveGenerator {
    private readonly Random rand;
    private readonly MC.IPolicy mcPolicy;
    private readonly TLR.IPolicy tlrPolicy;
    private readonly List<ICraneMoveEvent> moves;
    private readonly List<ICraneAgent> agents;
    private readonly World world;
    private readonly CraneSchedulingSimulation sim;

    private int moveId;

    public MoveGenerator(
      int seed,
      MC.IPolicy mcPolicy,
      TLR.IPolicy tlrPolicy,
      List<ICraneMoveEvent> moves,
      List<ICraneAgent> agents,
      World world,
      CraneSchedulingSimulation sim
    ) {
      rand = new Random(seed);

      this.mcPolicy = mcPolicy;
      this.tlrPolicy = tlrPolicy;
      this.moves = moves;
      this.agents = agents;
      this.world = world;
      this.sim = sim;
    }

    public void Update() {
      var executingMoveEvents = agents.Cast<CSCraneAgent>().OrderBy(x => x.Priority).Select(x => x.CurrentMove).Where(x => x != null).ToArray();
      var executingMoves = world.CraneMoves.Where(x => executingMoveEvents.Any(y => y.Id == x.Id)).ToArray();
      var generatedMoves = world.CraneMoves.Where(x => x.Id < 0).Except(executingMoves).OrderByDescending(x => x.Id).ToArray();

      var oldMoves = executingMoves.Concat(generatedMoves).ToArray();
      var moveRequests = world.MoveRequests.ToDictionary(x => x.BlockId);
      var locations = world.Locations.ToDictionary(x => x.Id, x => (Blocks: x.Stack.BottomToTop.Select(y => y.Id).ToList(), x.MaxHeight));

      var newMoves = new List<CraneMove>();
      var invalidMoves = new List<CraneMove>();

      // try to apply all pending moves
      foreach (var m in oldMoves) {
        if (ApplyMove(m, locations, executingMoves, moveRequests)) newMoves.Add(m);
        else invalidMoves.Add(m);
      }

      var storageLookup = new Dictionary<MoveRequest, (int Location, int Index)>();

      // generate moves for open move requests
      foreach (var mr in moveRequests.Values.ToArray()) {
        var blockId = mr.BlockId;
        var storedAt = locations.Select(x => (Location: x, Index: x.Value.Blocks.IndexOf(blockId))).FirstOrDefault(x => x.Index >= 0);
        if (storedAt.Location.Value.Blocks == null && !executingMoves.Any(x => x.MovedBlockIds.Contains(blockId))) {
          moveRequests.Remove(mr.BlockId);
          world.MoveRequests.Remove(mr);
          continue;
        }

        storageLookup.Add(mr, (storedAt.Location.Key, storedAt.Index));
      }

      // move requests ordered per location
      var orderedMoveRequests = moveRequests.Values
        .GroupBy(x => storageLookup[x].Location)
        .ToDictionary(x => x.Key, x => x.OrderByDescending(y => storageLookup[y].Index));

      var newMovesCount = newMoves.Count;

      foreach (var entry in orderedMoveRequests) {
        foreach (var mr in entry.Value) {
          GenerateMoves(mr, locations, newMoves, entry.Key);
        }
      }

      // update moves
      var invalidMoveIds = new HashSet<int>(invalidMoves.Select(x => x.Id));
      world.CraneMoves.RemoveAll(x => invalidMoveIds.Contains(x.Id));
      moves.RemoveAll(x => invalidMoveIds.Contains(x.Id));

      foreach (var m in newMoves.Skip(newMovesCount)) {
        world.CraneMoves.Add(m);
        moves.Add(new CraneMoveEvent(sim.Environment, m, true));
      }

      FixPrecedences(newMoves);
      FixPositions(newMoves);
    }

    private void FixPositions(List<CraneMove> moves) {
      var locIdToLocPos = world.Locations.ToDictionary(x => x.Id, x => x.GirderPosition);

      foreach (var m in moves) {
        if (locIdToLocPos.TryGetValue(m.PickupLocationId, out var pickPos))
          m.PickupGirderPosition = pickPos;
        else throw new InvalidOperationException("unknown location id");

        if (locIdToLocPos.TryGetValue(m.DropoffLocationId, out var dropPos))
          m.DropoffGirderPosition = dropPos;
        else throw new InvalidOperationException("unknown location id");
      }
    }

    private void FixPrecedences(List<CraneMove> moves) {
      var handoverLocIds = new HashSet<int>(world.HandoverLocations.Select(x => x.Id));
      var matDict = new Dictionary<int, int>(); // matId -> moveId
      var locDict = new Dictionary<int, int>(); // locId -> moveId

      foreach (var m in moves) {
        m.PredecessorIds.Clear();

        if (m.MovedBlockIds.Any()) {
          var blockId = m.MovedBlockIds.Single();
          if (matDict.TryGetValue(blockId, out var matPred)) {
            m.PredecessorIds.Add(matPred);
          }
          matDict[blockId] = m.Id;
        }


        var srcLocId = m.PickupLocationId;
        if (locDict.TryGetValue(srcLocId, out var srcLocPred)) {
          m.PredecessorIds.Add(srcLocPred);
        }
        locDict[srcLocId] = m.Id;

        var tgtLocId = m.DropoffLocationId;
        if (!handoverLocIds.Contains(tgtLocId)) {
          if (locDict.TryGetValue(tgtLocId, out var tgtLocPred)) {
            m.PredecessorIds.Add(tgtLocPred);
          }
          locDict[tgtLocId] = m.Id;
        }
      }
    }

    public void GenerateMoves(
      MoveRequest moveReq,
      Dictionary<int, (List<int> Blocks, int MaxHeight)> locations,
      List<CraneMove> existingMoves,
      int srcLocId
    ) {
      var loc = world.Locations.Single(x => x.Id == srcLocId);
      if (loc.Type == StackTypes.HandoverStack) return;
      if (moveReq.TargetLocationId == srcLocId) return;

      var blockId = moveReq.BlockId;
      var srcLoc = locations[srcLocId];
      var moves = new List<CraneMove>();
      var predecessors = existingMoves.Where(x => x.PickupLocationId == srcLocId).ToList();
      var storedAbove = Enumerable.Reverse(srcLoc.Blocks).TakeWhile(x => x != blockId).ToList();

      foreach (var b in storedAbove) {
        var relTgtLoc = GetLocation(srcLocId, locations, existingMoves);
        if (relTgtLoc == null) throw new NotImplementedException("could not determine target location");
        var relBlockIds = new List<int> { b };

        var relocation = new CraneMove {
          Id = --moveId,
          Type = MoveType.PickupAndDropoff,
          PickupLocationId = srcLocId,
          DropoffLocationId = relTgtLoc.Id,
          Amount = relBlockIds.Count,
          ReleaseTime = world.Now,
          DueDate = moveReq.DueDate,
          PredecessorIds = new HashSet<int>(moves.Select(x => x.Id)),
          MovedBlockIds = relBlockIds
        };

        moves.Add(relocation);
        if (ApplyMove(relocation, locations)) existingMoves.Add(relocation);
        else throw new InvalidOperationException("could not apply moves");
      }

      predecessors.AddRange(moves);

      var moveBlockIds = new List<int> { blockId };
      var tgtLocId = moveReq.TargetLocationId;
      if (tgtLocId < 0) {
        var tgtLoc = GetLocation(srcLocId, locations, existingMoves);
        if (tgtLoc == null) throw new NotImplementedException("could not determine target location");
        tgtLocId = tgtLoc.Id;
      }

      var move = new CraneMove {
        Id = --moveId,
        Type = MoveType.PickupAndDropoff,
        PickupLocationId = srcLocId,
        DropoffLocationId = tgtLocId,
        Amount = moveBlockIds.Count,
        ReleaseTime = world.Now,
        DueDate = new TimeStamp((long)Math.Floor(TimeSpan.MaxValue.TotalMilliseconds)),
        PredecessorIds = new HashSet<int>(predecessors.Select(x => x.Id)),
        MovedBlockIds = moveBlockIds
      };

      if (ApplyMove(move, locations)) existingMoves.Add(move);
      else throw new InvalidOperationException("could not apply moves");
    }

    private static bool ApplyMove(
      CraneMove m,
      Dictionary<int, (List<int> Blocks, int MaxHeight)> locations,
      CraneMove[] executingMoves = null,
      Dictionary<int, MoveRequest> moveRequests = null
    ) {
      if (!m.MovedBlockIds.Any()) return true;

      var blockId = m.MovedBlockIds.Single();

      var (tgtBlocks, maxHeight) = locations[m.DropoffLocationId];

      var (srcBlocks, _) = locations[m.PickupLocationId];
      var isAvailable = srcBlocks.Any() && srcBlocks.Last() == blockId;
      var isExecuting = executingMoves != null && executingMoves.Contains(m);

      if (isAvailable) {
        srcBlocks.RemoveAt(srcBlocks.Count - 1);
      } else if (!isExecuting) {
        return false;
      }

      if (!tgtBlocks.Any() || tgtBlocks.Last() != blockId) {
        if (tgtBlocks.Count >= maxHeight) return false;
        tgtBlocks.Add(blockId);
      }

      if (moveRequests != null && moveRequests.TryGetValue(blockId, out var mr)) {
        if (mr.TargetLocationId == -1 || mr.TargetLocationId == m.DropoffLocationId) {
          moveRequests.Remove(blockId);
        }
      }

      return true;
    }

    public Location GetLocation(
      int srcLocId,
      Dictionary<int, (List<int> Blocks, int MaxHeight)> locations,
      List<CraneMove> existingMoves
    ) {
      var usedLocs = new HashSet<int> { srcLocId };

      foreach (var move in existingMoves) {
        usedLocs.Add(move.PickupLocationId);
        usedLocs.Add(move.DropoffLocationId);
      }

      var srcLoc = world.Locations.SingleOrDefault(x => x.Id == srcLocId);
      var freeTargets = world.BufferLocations.Where(x =>
        x.FreeHeight > 0 &&
        world.Cranes.Any(y =>
          y.CanReach(srcLoc.GirderPosition) &&
          y.CanReach(x.GirderPosition)))
        .ToDictionary(x => x.Id);

      var choices = freeTargets.Keys.Except(usedLocs).ToList();

      if (!choices.Any()) return null;

      var choice = choices[rand.Next(choices.Count)];
      return freeTargets[choice];
    }
  }
}
