using System;

namespace DynStack.DataModel.Common {
  public interface IPerformance : IComparable {
    object[] ObjectiveValues { get; }
  }
}
