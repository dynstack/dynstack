using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel.RM;
using SimSharp;

namespace DynStack.Simulation.RM {

  public interface IPolicy {
    PlannedCraneMoves CalculateMoves(World world);
  }

  public class BasicSortingPolicy : IPolicy {
    protected int seqNr = 0;
    protected int planNr = 0;

    public virtual PlannedCraneMoves CalculateMoves(World world) {
      var plan = new PlannedCraneMoves() {
        SequenceNr = planNr++,
        Moves = new List<CraneMove>(world.CraneMoves.Moves)
      };

      if (plan.Moves.All(x => x.RequiredCraneId != world.ShuffleCrane.Id)) {
        MovesForShuffleCrane(world, plan);
      }
      if (plan.Moves.All(x => x.RequiredCraneId != world.HandoverCrane.Id)) {
        MovesForHandoverCrane(world, plan);
      }

      return plan;
    }

    private void MovesForShuffleCrane(World world, PlannedCraneMoves plan) {
      var craneCap = world.ShuffleCrane.CraneCapacity;
      var craneCap1 = Math.Max(1, craneCap - 1);

      // priority locations are those that contain blocks that are soon to be processed
      //var prios = world.Locations.Where(x => x.Type == StackTypes.ArrivalStack || x.Type == StackTypes.ShuffleBuffer)
      //                           .Select(x => new { Location = x, Prio = x.Stack.BottomToTop.Any(y => y.Sequence <= craneCap) ? 0 : 1 })
      //                           .ToDictionary(x => x.Location, x => x.Prio);
      //var minPrio = prios.Values.Min();
      var shuffleTargets = world.Locations.Where(x => x.Type == StackTypes.ShuffleBuffer && !IsMovePlanned(x, plan)).ToList();
      // PRIO 1: Admit blocks from the arrival stack and move them to the shuffle buffer
      //         Note that when lots arrive pre-sorted, these could potentially be transported from arrival to sorted right away
      var admit = false;
      if (world.Locations.Any(x => x.Type == StackTypes.ArrivalStack && x.Height > 0 && !IsMovePlanned(x, plan))) {
        var arrivalLots = Enumerable.Range(1, craneCap)
          .SelectMany(lotsize => world.Locations.Where(x =>
              x.Type == StackTypes.ArrivalStack
              && x.Height >= lotsize
              && !IsMovePlanned(x, plan)
              && IsDecreasingSequenceOfSameMill(x, lotsize)
          ).Select(x => (Location: x, Lot: x.Stack.TopToBottom.Take(lotsize).ToList()))).ToList();
        var admissionMove = ArrivalToShuffle(shuffleTargets, world.ShuffleCrane, arrivalLots);
        if (admissionMove != null) {
          admissionMove.ReleaseTime = world.Now;
          plan.Moves.Add(admissionMove);
          admit = true;
        }
      }
      if (!admit) {
        var sortedTargets = world.Locations.Where(x => x.Type == StackTypes.SortedBuffer && !IsMovePlanned(x, plan)).ToList();
        // PRIO 2: Move bigger lots from the shuffle area to the sorted area using the shuffle crane
        //         The handover crane will be used to transport smaller lots
        var shuffleBufferLots = Enumerable
            .Range(craneCap1, Math.Min(2, world.ShuffleCrane.CraneCapacity))
            .SelectMany(lotsize => world.Locations.Where(x =>
              x.Type == StackTypes.ShuffleBuffer
              && x.Height >= lotsize
              && !IsMovePlanned(x, plan)
              && IsDecreasingSequenceOfSameMill(x, lotsize))
            .Select(x => (Location: x, Lot: x.Stack.TopToBottom.Take(lotsize).ToList()))).ToList();

        var sortMove = ShuffleToSorted(sortedTargets, world.ShuffleCrane, shuffleBufferLots);
        if (sortMove != null) {
          sortMove.ReleaseTime = world.Now;
          plan.Moves.Add(sortMove);
        } else {
          // PRIO 3: Consolidate the shuffle buffer
          var shuffleLots = Enumerable.Range(1, craneCap)
            .SelectMany(lotsize =>
              world.Locations.Where(x => x.Type == StackTypes.ShuffleBuffer
                                      && x.Height >= lotsize
                                      && !IsMovePlanned(x, plan)
                                      && IsDecreasingSequenceOfSameMill(x, lotsize))
                              .Select(x => (Location: x, Lot: x.Stack.TopToBottom.Take(lotsize).ToList()))
            ).ToList();

          var shuffleMove = ShuffleToShuffle(shuffleTargets, world.ShuffleCrane, shuffleLots);
          if (shuffleMove != null) {
            shuffleMove.ReleaseTime = world.Now;
            plan.Moves.Add(shuffleMove);
          }
        }
      }
    }

    private void MovesForHandoverCrane(World world, PlannedCraneMoves plan) {
      var sortedTargets = world.Locations.Where(x => x.Type == StackTypes.SortedBuffer && !IsMovePlanned(x, plan)).ToList();

      // PRIO 1: Move blocks from shuffle buffer to sorted buffer
      var shuffleBufferLots = Enumerable.Range(1, world.HandoverCrane.CraneCapacity)
                .SelectMany(lotsize => world.Locations.Where(x => x.Type == StackTypes.ShuffleBuffer && x.Height >= lotsize && !IsMovePlanned(x, plan) && IsDecreasingSequenceOfSameMill(x, lotsize))
                .Select(x => (Location: x, Lot: x.Stack.TopToBottom.Take(lotsize).ToList()))).ToList();

      var move = ShuffleToSorted(sortedTargets, world.HandoverCrane, shuffleBufferLots);
      if (move != null) {
        move.ReleaseTime = world.Now;
        plan.Moves.Add(move);
      } else {
        // PRIO 2: Rearrange and consolidate sorted buffer
        var sortingLots = Enumerable.Range(1, world.HandoverCrane.CraneCapacity)
        .SelectMany(lotsize => world.Locations.Where(x => x.Type == StackTypes.SortedBuffer && x.Height >= lotsize && !IsMovePlanned(x, plan) && IsDecreasingSequenceOfSameMill(x, lotsize))
        .Select(x => (Location: x, Lot: x.Stack.TopToBottom.Take(lotsize).ToList()))).ToList();
        var sortMove = SortedToSorted(sortedTargets, world.HandoverCrane, sortingLots);
        if (sortMove != null) {
          sortMove.ReleaseTime = world.Now;
          plan.Moves.Add(sortMove);
        }
      }
    }

    private CraneMove ArrivalToShuffle(List<Location> targetLocations, Crane crane, List<(Location Location, List<Block> Lot)> arrivalLots) {
      var craneCap = crane.CraneCapacity;
      var craneCap1 = Math.Max(1, craneCap - 1);
      var bestCriterion = double.MaxValue;
      Location bestSrcLoc = null, bestTgtLoc = null;
      int bestAmt = 0;
      foreach (var lot in arrivalLots) {
        foreach (var sb in targetLocations.Where(x =>
            x.FreeHeight >= lot.Lot.Count
            && IsEmptyOrTopNAreOfSameMill(x, lot.Lot[0].Type, N: craneCap1)
            && !x.Stack.BottomToTop.Any(y => y.Sequence <= crane.CraneCapacity)
        )) {
          var sbtopLot = sb.Stack.TopToBottom.TakeWhile(x => x.Type == sb.Topmost.Type).Take(craneCap).ToList();
          if (lot.Lot.Count < craneCap && sb.Height > 0 && sb.Topmost.Type == lot.Lot[0].Type
            && lot.Lot.Max(x => x.Sequence) > sbtopLot.Take(craneCap - lot.Lot.Count).Min(x => x.Sequence))
            continue;
          double criterion;
          if (sb.Height == 0) criterion = 1500 - lot.Lot.Count; // prefer empty stack when a perfect sequence match cannot be achieved
          else if (sb.Topmost.Type != lot.Lot[0].Type) criterion = (craneCap * 1000) + 750 - lot.Lot.Count; // prefer to mix types only when sequence difference is > scap
          else criterion = Math.Abs(lot.Lot[0].Sequence - sb.Topmost.Sequence) * 1000 - (sb.Height + lot.Lot.Count); // try to minimize sequence match
          if (criterion < bestCriterion || criterion == bestCriterion && bestAmt < lot.Lot.Count) {
            bestSrcLoc = lot.Location;
            bestTgtLoc = sb;
            bestAmt = lot.Lot.Count;
          }
        }
      }
      if (bestAmt > 0) {
        return new CraneMove() {
          Id = seqNr++,
          Amount = bestAmt,
          PickupLocationId = bestSrcLoc.Id,
          PickupGirderPosition = bestSrcLoc.GirderPosition,
          DropoffLocationId = bestTgtLoc.Id,
          DropoffGirderPosition = bestTgtLoc.GirderPosition,
          Type = DataModel.MoveType.PickupAndDropoff,
          RequiredCraneId = crane.Id
        };
      }
      return null;
    }

    private CraneMove ShuffleToShuffle(List<Location> targetLocations, Crane crane, List<(Location Location, List<Block> Lot)> shuffleLots) {
      var craneCap = crane.CraneCapacity;
      var craneCap1 = Math.Max(1, craneCap - 1);
      var bestCriterion = double.MaxValue;
      Location bestSrcLoc = null, bestTgtLoc = null;
      var bestAmt = 0;
      foreach (var lot in shuffleLots) {
        foreach (var sb in targetLocations.Where(x =>
            x.Id != lot.Location.Id
            && x.Height > 0
            && x.FreeHeight >= lot.Lot.Count
        )) {
          var sbtopLot = sb.Stack.TopToBottom.TakeWhile(x => x.Type == sb.Topmost.Type).Take(craneCap).ToList();
          if (sbtopLot.Count < craneCap1 && sb.Topmost.Type != lot.Lot[0].Type)
            continue; // The current lot (=blocks of same type) is too small to start a new one

          if (sb.Topmost.Type != lot.Lot[0].Type && lot.Location.Stack.BottomToTop.Any(x => x.Type == sb.Topmost.Type) && sb.Stack.BottomToTop.Where(x => x.Type == sb.Topmost.Type).Min(x => x.Sequence) > lot.Location.Stack.BottomToTop.Where(x => x.Type == sb.Topmost.Type).Min(x => x.Sequence) // we put it atop an existing lot of the other mill
              || ((lot.Lot.Count >= craneCap1 || sbtopLot.Take(craneCap1 - lot.Lot.Count).Min(x => x.Sequence) > lot.Lot.Max(x => x.Sequence)) // the lot is sufficiently big or adheres to decreasing sequence
                && (lot.Location.Height == lot.Lot.Count || lot.Location.Stack.TopToBottom.Skip(lot.Lot.Count).First().Type != sb.Topmost.Type || Math.Abs(sb.Topmost.Sequence - lot.Lot[0].Sequence) < Math.Abs(lot.Location.Stack.TopToBottom.Skip(lot.Lot.Count).First().Sequence - lot.Lot[0].Sequence)) // or the sequence difference is smaller
                 )) {
            var criterion = (sb.Topmost.Type == lot.Lot[0].Type ? 0 : 1) * 1000
              + Math.Abs(sb.Topmost.Sequence - lot.Lot[0].Sequence) * 100
              + (sb.FreeHeight - lot.Lot.Count);
            if (criterion < bestCriterion || criterion == bestCriterion && bestAmt < lot.Lot.Count) {
              bestSrcLoc = lot.Location;
              bestTgtLoc = sb;
              bestAmt = lot.Lot.Count;
            }
          }
        }
      }
      if (bestAmt > 0) {
        return new CraneMove() {
          Id = seqNr++,
          Amount = bestAmt,
          PickupLocationId = bestSrcLoc.Id,
          PickupGirderPosition = bestSrcLoc.GirderPosition,
          DropoffLocationId = bestTgtLoc.Id,
          DropoffGirderPosition = bestTgtLoc.GirderPosition,
          Type = DataModel.MoveType.PickupAndDropoff,
          RequiredCraneId = crane.Id
        };
      }
      return null;
    }

    private CraneMove ShuffleToSorted(List<Location> targetLocations, Crane crane, List<(Location Location, List<Block> Lot)> shuffleBufferLots) {
      // CASE 1a: ShuffleBuffer -> SortedBuffer (add to existing location)
      // see if we can fit that lot onto a used stack
      if (shuffleBufferLots.Count > 0) {
        var bestCriterion = double.MaxValue;
        Location bestSrcLoc = null, bestTgtLoc = null;
        int bestAmt = 0;
        foreach (var lot in shuffleBufferLots) {
          foreach (var so in targetLocations.Where(x => 
              x.Height > 0 // already contains blocks
              && x.FreeHeight >= lot.Lot.Count // there is enough space
              && x.Topmost.Type == lot.Lot[0].Type)) { // the blocks are for the same mill
            if (so.Stack.BottomToTop.Min(x => x.Sequence) > lot.Lot.Max(x => x.Sequence)) {
              var criterion = so.FreeHeight - lot.Lot.Count;
              if (criterion < bestCriterion || criterion == bestCriterion && bestAmt < lot.Lot.Count) {
                bestSrcLoc = lot.Location;
                bestTgtLoc = so;
                bestAmt = lot.Lot.Count;
              }
            }
          }
        }
        if (bestAmt > 0) {
          return new CraneMove() {
            Id = seqNr++,
            Amount = bestAmt,
            PickupLocationId = bestSrcLoc.Id,
            PickupGirderPosition = bestSrcLoc.GirderPosition,
            DropoffLocationId = bestTgtLoc.Id,
            DropoffGirderPosition = bestTgtLoc.GirderPosition,
            Type = DataModel.MoveType.PickupAndDropoff,
            RequiredCraneId = crane.Id
          };
        } else {
          // CASE 1b: ShuffleBuffer -> SortedBuffer (use new location)
          var biggest = shuffleBufferLots.MaxItems(x => x.Lot.Count).First();
          var emptyLoc = targetLocations.Where(x => x.Height == 0 && x.FreeHeight >= biggest.Lot.Count).FirstOrDefault();
          if (emptyLoc != null) {
            return new CraneMove() {
              Id = seqNr++,
              Amount = biggest.Lot.Count,
              PickupLocationId = biggest.Location.Id,
              PickupGirderPosition = biggest.Location.GirderPosition,
              DropoffLocationId = emptyLoc.Id,
              DropoffGirderPosition = emptyLoc.GirderPosition,
              Type = DataModel.MoveType.PickupAndDropoff,
              RequiredCraneId = crane.Id
            };
          }
        }
      }
      return null;
    }

    private CraneMove SortedToSorted(List<Location> targetLocations, Crane crane, List<(Location Location, List<Block> Lot)> sortingLots) {
      var bestCriterion = double.MaxValue;
      Location bestSrcLoc = null, bestTgtLoc = null;
      int bestAmt = 0;
      foreach (var lot in sortingLots) {
        foreach (var so in targetLocations.Where(x =>
            x.Height > 0 // already contains blocks
            && x.FreeHeight >= lot.Lot.Count // there is enough space
            && x.Topmost.Type == lot.Lot[0].Type)) { // the blocks are for the same mill
          if (so.Stack.BottomToTop.Min(x => x.Sequence) > lot.Lot.Max(x => x.Sequence)
              && so.Height + lot.Lot.Count > lot.Location.Height) { // the new stack must be bigger than the old
            var criterion = so.FreeHeight - lot.Lot.Count;
            if (criterion < bestCriterion || criterion == bestCriterion && bestAmt < lot.Lot.Count) {
              bestSrcLoc = lot.Location;
              bestTgtLoc = so;
              bestAmt = lot.Lot.Count;
            }
          }
        }
      }
      if (bestAmt > 0) {
        return new CraneMove() {
          Id = seqNr++,
          Amount = bestAmt,
          PickupLocationId = bestSrcLoc.Id,
          PickupGirderPosition = bestSrcLoc.GirderPosition,
          DropoffLocationId = bestTgtLoc.Id,
          DropoffGirderPosition = bestTgtLoc.GirderPosition,
          Type = DataModel.MoveType.PickupAndDropoff,
          RequiredCraneId = crane.Id
        };
      }
      return null;
    }

    private static bool IsEmptyOrTopNAreOfSameMill(Location x, MillTypes type, int N) {
      return x.Height == 0
        || (x.Stack.TopToBottom.Take(N).All(y => y.Type == x.Topmost.Type)
           && x.Stack.Size >= N) // the top N are all of the same type, meaning the given type doesn't matter
        || x.Stack.Topmost.Type == type; // or the top is of the given type
    }

    protected static bool IsDecreasingSequenceOfSameMill(Location x, int lotsize) {
      if (lotsize == 1) return true;
      var b1 = x.Stack.BottomToTop[x.Height - lotsize];
      for (var i = x.Height - lotsize + 1; i < x.Height; i++) {
        var b2 = x.Stack.BottomToTop[i];
        if (b2.Type != b1.Type || b2.Sequence > b1.Sequence) return false;
        b1 = b2;
      }
      return true;
    }

    protected static bool IsMovePlanned(Location x, PlannedCraneMoves plan) {
      return plan.Moves.Any(y => x.Id == y.PickupLocationId || x.Id == y.DropoffLocationId);
    }
  }

  public class BasicSortingPolicyWithHandover : BasicSortingPolicy {
    public override PlannedCraneMoves CalculateMoves(World world) {
      var plan = base.CalculateMoves(world);

      var handoverA = world.Locations.Single(x => x.Type == StackTypes.HandoverStack && x.MillType == MillTypes.A);
      var handoverB = world.Locations.Single(x => x.Type == StackTypes.HandoverStack && x.MillType == MillTypes.B);

      if (handoverA.Height == 0 && NoMoveToHandover(world, plan, handoverA)) {
        var nextMillA = world.AllBlocks().SingleOrDefault(x => x.Type == MillTypes.A && x.Sequence == 1);
        if (nextMillA != null) {
          var loc = world.Locations.SingleOrDefault(x => x.Height > 0 && x.Topmost.Id == nextMillA.Id);
          if (loc != null && world.HandoverCrane.CanReach(loc.GirderPosition) && !IsMovePlanned(loc, plan)) {
            var seq = nextMillA.Sequence;
            var size = 1;
            if (world.HandoverCrane.CraneCapacity > 1) {
              foreach (var b in loc.Stack.TopToBottom.Skip(1).Take(world.HandoverCrane.CraneCapacity - 1)) {
                if (b.Type == MillTypes.B || b.Sequence > seq + 1) break;
                seq++;
                size++;
              }
            }
            plan.Moves.RemoveAll(x => x.RequiredCraneId == world.HandoverCrane.Id && x.DropoffLocationId != handoverA.Id && x.DropoffLocationId != handoverB.Id);
            plan.Moves.Add(new CraneMove() {
              Id = seqNr++,
              Amount = size,
              PickupLocationId = loc.Id,
              PickupGirderPosition = loc.GirderPosition,
              DropoffLocationId = handoverA.Id,
              DropoffGirderPosition = handoverA.GirderPosition,
              ReleaseTime = world.Now,
              Type = DataModel.MoveType.PickupAndDropoff,
              RequiredCraneId = world.HandoverCrane.Id
            });
          }
        }
      }

      if (handoverB.Height == 0 && NoMoveToHandover(world, plan, handoverB)) {
        var nextMillB = world.AllBlocks().SingleOrDefault(x => x.Type == MillTypes.B && x.Sequence == 1);
        if (nextMillB != null) {
          var loc = world.Locations.SingleOrDefault(x => x.Height > 0 && x.Topmost.Id == nextMillB.Id);
          if (loc != null && world.HandoverCrane.CanReach(loc.GirderPosition) && !IsMovePlanned(loc, plan)) {
            var seq = nextMillB.Sequence;
            var size = 1;
            if (world.HandoverCrane.CraneCapacity > 1) {
              foreach (var b in loc.Stack.TopToBottom.Skip(1).Take(world.HandoverCrane.CraneCapacity - 1)) {
                if (b.Type == MillTypes.A || b.Sequence > seq + 1) break;
                seq++;
                size++;
              }
            }
            plan.Moves.RemoveAll(x => x.RequiredCraneId == world.HandoverCrane.Id && x.DropoffLocationId != handoverA.Id && x.DropoffLocationId != handoverB.Id);
            plan.Moves.Add(new CraneMove() {
              Id = seqNr++,
              Amount = size,
              PickupLocationId = loc.Id,
              PickupGirderPosition = loc.GirderPosition,
              DropoffLocationId = handoverB.Id,
              DropoffGirderPosition = handoverB.GirderPosition,
              ReleaseTime = world.Now,
              Type = DataModel.MoveType.PickupAndDropoff,
              RequiredCraneId = world.HandoverCrane.Id
            });
          }
        }
      }
      return plan;
    }

    private static bool NoMoveToHandover(World world, PlannedCraneMoves plan, Location handover) {
      return !plan.Moves.Any(x => x.RequiredCraneId == world.HandoverCrane.Id && x.DropoffLocationId == handover.Id);
    }
  }
}
