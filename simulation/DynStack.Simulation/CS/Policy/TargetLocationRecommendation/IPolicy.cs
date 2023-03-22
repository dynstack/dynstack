using DynStack.DataModel.CS;

namespace DynStack.Simulation.CS.Policy.TargetLocationRecommendation {
  public interface IPolicy {
    Location GetLocation(Block block, World world);
  }
}
