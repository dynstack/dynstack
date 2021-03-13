using DynStack.DataModel.Common;

namespace DynStack.DataModel {
  public interface IWorld : ISerializable {
    /// <summary>
    /// The current time
    /// </summary>
    TimeStamp Now { get; set; }

    IPerformance KPIs { get; }
  }
}