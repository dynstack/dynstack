using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel.CS;

namespace DynStack.Simulation.CS.Policy.TargetLocationRecommendation {
  public class RandomPolicy : IPolicy {
    private readonly Random rand;

    public RandomPolicy(int seed) {
      rand = new Random(seed);
    }

    public Location GetLocation(Block block, World world) {
      var usedLocs = new HashSet<int>();

      foreach (var move in world.CraneMoves) {
        usedLocs.Add(move.PickupLocationId);
        usedLocs.Add(move.DropoffLocationId);
      }

      var srcLoc = world.Locations.SingleOrDefault(x => x.Stack.BottomToTop.Contains(block));
      var freeTargets = world.BufferLocations.Where(x => x.FreeHeight > 0 && world.Cranes.Any(y => y.CanReach(srcLoc.GirderPosition) && y.CanReach(x.GirderPosition))).ToDictionary(x => x.Id);
      var choices = freeTargets.Keys.Except(usedLocs).ToList();

      var choice = choices[rand.Next(choices.Count)];
      return freeTargets[choice];
    }
  }
}
