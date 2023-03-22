using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel.HS;

namespace DynStack.Simulation.HS {
  public class RuleBasedCranePolicy : IPolicy {
    private int seqNr = 0;

    public CraneSchedule GetSchedule(World world) {
      if (world.Buffers == null || (world.Crane.Schedule.Moves?.Count ?? 0) > 0) return null;

      var schedule = new CraneSchedule() { SequenceNr = seqNr++, Moves = new List<CraneMove>() };

      var topReadyStacks = world.Buffers.Concat(new[] { world.Production }).Where(x => x.Height > 0 && x.BottomToTop.Last().Ready).ToList();
      var emptyStack = world.Buffers.FirstOrDefault(x => x.BottomToTop == null || x.Height == 0);
      var remainingCapacity = world.Buffers.Sum(x => x.MaxHeight) - world.Buffers.Sum(x => x.Height);

      var possibleRemoves = world.Handover.Ready && world.Handover.Block == null && topReadyStacks.Count > 0;
      var prioritizeProduction = remainingCapacity >= world.Buffers.Count && world.Production.Height >= world.Production.MaxHeight;

      if (prioritizeProduction) {
        TryAddPutMoves(world, schedule, emptyStack, possibleRemoves);
      }
      if (schedule.Moves.Count == 0 && possibleRemoves) {
        TryAddRemoveMoves(world, schedule, topReadyStacks);
      } else if (schedule.Moves.Count == 0) {
        if (!prioritizeProduction) TryAddPutMoves(world, schedule, emptyStack, possibleRemoves);
        if (schedule.Moves.Count == 0) { // no possible PUT move
          TryAddRelocateMoves(world, schedule, emptyStack);
        }
      }
      return schedule.Moves.Count > 0 ? schedule : null;
    }

    private static void TryAddRemoveMoves(World world, CraneSchedule schedule, List<Stack> topReadyStacks) {
      var next = topReadyStacks.Select(x => x.BottomToTop.Last()).OrderBy(x => x.Due).First();
      //REMOVE: Remove non-obstructed block
      schedule.Moves.Add(new CraneMove {
        BlockId = next.Id,
        Sequence = 0,
        SourceId = topReadyStacks.Single(x => x.BottomToTop.Last() == next).Id,
        TargetId = world.Handover.Id,
      });
    }

    private static void TryAddPutMoves(World world, CraneSchedule schedule, Stack emptyStack, bool possibleRemoves) {
      var newBlock = world.Production.BottomToTop?.LastOrDefault();
      if (newBlock != null) {
        if (newBlock.Ready && world.Handover.Ready) {
          //PUT1: Move new (ready) block to handover
          schedule.Moves.Add(new CraneMove {
            BlockId = newBlock.Id,
            Sequence = 0,
            SourceId = world.Production.Id,
            TargetId = world.Handover.Id
          });
        } else if (!newBlock.Ready && emptyStack != null) {
          //PUT2: Moving new block to empty stack
          schedule.Moves.Add(new CraneMove {
            BlockId = newBlock.Id,
            Sequence = 0,
            SourceId = world.Production.Id,
            TargetId = emptyStack.Id
          });
        } else if (!newBlock.Ready && emptyStack == null) {
          var alternative = world.Buffers.Where(x => !x.BottomToTop.Any(y => y.Ready) && x.Height < x.MaxHeight)
            .OrderBy(x => x.Height).FirstOrDefault();
          if (alternative != null) {
            //PUT3: Moving new block to smallest stack without any ready ones
            schedule.Moves.Add(new CraneMove {
              BlockId = newBlock.Id,
              Sequence = 0,
              SourceId = world.Production.Id,
              TargetId = alternative.Id
            });
          }
        } else if (newBlock.Ready && emptyStack == null && !possibleRemoves) {
          var alternative = world.Buffers.Where(x => x.Height < x.MaxHeight)
            .OrderByDescending(x => x.Height).FirstOrDefault();
          if (alternative != null) {
            //PUT4: Moving new (ready) block to largest stack
            schedule.Moves.Add(new CraneMove {
              BlockId = newBlock.Id,
              Sequence = 0,
              SourceId = world.Production.Id,
              TargetId = alternative.Id
            });
          }
        }
      }
    }

    private static void TryAddRelocateMoves(World world, CraneSchedule schedule, Stack emptyStack) {
      var blockAtBuffer = (from stack in world.Buffers
                           from block in stack.BottomToTop ?? Enumerable.Empty<Block>()
                           select (stack, block)).ToDictionary(x => x.block, x => x.stack);

      var blocksReady = blockAtBuffer.Keys.Where(x => x.Ready).ToList();
      if (blocksReady.Count == 0) return;
      var bestPositions = blocksReady.Select(x => new {
        Block = x,
        ObstructedBy = blockAtBuffer[x].Height - blockAtBuffer[x].BottomToTop.IndexOf(x) - 1
      }).OrderBy(x => x.ObstructedBy).ThenBy(x => blockAtBuffer[x.Block].Height).ThenBy(x => x.Block.Due).ToList();

      foreach (var pos in bestPositions) {

        if (pos.ObstructedBy > 0) {
          var top = blockAtBuffer[pos.Block].BottomToTop.Last();
          if (emptyStack != null) {
            //RELOCATE1: Move obstructed to empty stack
            schedule.Moves.Add(new CraneMove {
              BlockId = top.Id,
              Sequence = 0,
              SourceId = blockAtBuffer[top].Id,
              TargetId = emptyStack.Id
            });
            break;
          } else {
            var alternative = world.Buffers.Where(x => !x.BottomToTop.Any(y => y.Ready) && x.Height < x.MaxHeight)
              .OrderBy(x => x.Height).FirstOrDefault();
            if (alternative != null) {
              //RELOCATE2: Move obstructed to smallest stack without any ready ones
              schedule.Moves.Add(new CraneMove {
                BlockId = top.Id,
                Sequence = 0,
                SourceId = blockAtBuffer[top].Id,
                TargetId = alternative.Id
              });
              break;
            } else {
              var alternative2 = world.Buffers.Where(x => x.Id != blockAtBuffer[pos.Block].Id && x.MaxHeight - x.Height >= pos.ObstructedBy)
              .OrderByDescending(x => x.BottomToTop.Select((b, v) => new { Block = b, Pos = v }).Where(y => y.Block.Ready).Min(y => blockAtBuffer[y.Block].Height - y.Pos)).FirstOrDefault();
              if (alternative2 != null) {
                //RELOCATE3: Try to free the best possible ready one
                schedule.Moves.Add(new CraneMove {
                  BlockId = top.Id,
                  Sequence = 0,
                  SourceId = blockAtBuffer[top].Id,
                  TargetId = alternative2.Id
                });
                break;
              }
            }
          }
        }
      }
    }
  }
}

