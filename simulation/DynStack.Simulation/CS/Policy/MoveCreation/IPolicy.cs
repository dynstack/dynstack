using System.Collections.Generic;
using DynStack.DataModel.CS;

namespace DynStack.Simulation.CS.Policy.MoveCreation {
  public interface IPolicy {
    IEnumerable<CraneMove> GetMoves(Block block, World world);
  }
}
