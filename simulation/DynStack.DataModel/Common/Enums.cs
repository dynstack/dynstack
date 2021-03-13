using ProtoBuf;

namespace DynStack.DataModel {
  [ProtoContract] public enum MoveType {[ProtoEnum] MoveToPickup, [ProtoEnum] PickupAndDropoff }
}

namespace DynStack.DataModel.Common {
  public enum SimulationState { Started, Created, Aborted, Completed }
  public enum SimulationType { HS = 1, RM = 2, CS = 3 }
}
