using System.IO;
using ProtoBuf;

namespace DynStack.DataModel.Common {
  public static class SerializableExtensions {
    public static byte[] Serialize(this ISerializable obj) {
      using (var stream = new MemoryStream()) {
        Serializer.Serialize(stream, obj);
        return stream.ToArray();
      }
    }
    public static T Parse<T>(this byte[] data) where T : ISerializable {
      using (var stream = new MemoryStream(data)) {
        return Serializer.Deserialize<T>(stream);
      }
    }
  }
}
