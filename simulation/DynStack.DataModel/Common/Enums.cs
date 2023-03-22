using ProtoBuf;

namespace DynStack.DataModel {
  [ProtoContract] public enum CraneScheduleActivityState { [ProtoEnum] Created, [ProtoEnum] Activatable, [ProtoEnum] Active }
  [ProtoContract] public enum MoveType { [ProtoEnum] MoveToPickup, [ProtoEnum] PickupAndDropoff }
  [ProtoContract] public enum CraneMoveTermination { [ProtoEnum] Obsolete, [ProtoEnum] Invalid, [ProtoEnum] Success }
}

namespace DynStack.DataModel.Common {
  public enum SimulationState { Started, Created, Aborted, Completed }
  public enum SimulationType { HS = 1, RM = 2, CS = 3 }
}
