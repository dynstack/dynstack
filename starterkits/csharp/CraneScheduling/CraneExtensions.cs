using DynStacking.CraneScheduling.DataModel;

namespace DynStacking.CraneScheduling {
  internal static class CraneExtensions {
    internal static bool CanReach(this Crane crane, double girderPosition) =>
      crane.MinPosition <= girderPosition && girderPosition <= crane.MaxPosition;
  }
}
