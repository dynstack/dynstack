using ProtoBuf;

namespace DynStack.DataModel.Messages {
  [ProtoContract]
  public class SimControl {
    public const int START_SIM = 1;
    public const int STOP_SIM = 2;

    [ProtoMember(1)] public int Action { get; set; }
    [ProtoMember(2)] public string Id { get; set; }
    [ProtoMember(3)] public byte[] Settings { get; set; }

    public static SimControl Start(string id, byte[] settings) => new SimControl {
      Id = id,
      Action = START_SIM,
      Settings = settings,
    };
    public static SimControl Stop(string id) => new SimControl {
      Id = id,
      Action = STOP_SIM,
    };
  }
}
