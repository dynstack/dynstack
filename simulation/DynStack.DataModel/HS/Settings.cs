using System;
using DynStack.DataModel.Common;
using ProtoBuf;

namespace DynStack.DataModel.HS {
  [ProtoContract]
  public class Settings : ISettings {
    [ProtoMember(1)] public int ProductionMaxHeight { get; set; }
    [ProtoMember(2)] public int BufferMaxHeight { get; set; }
    [ProtoMember(3)] public int BufferCount { get; set; }

    [ProtoMember(6)] public TimeSpan SimulationDuration { get; set; }
    [ProtoMember(7)] public TimeSpan CheckInterval { get; set; }
    [ProtoMember(8)] public TimeSpan MinClearTime { get; set; }
    [ProtoMember(9)] public TimeSpan MaxClearTime { get; set; }
    [ProtoMember(10)] public TimeSpan CraneMoveTimeMean { get; set; }
    [ProtoMember(11)] public TimeSpan CraneMoveTimeStd { get; set; }
    [ProtoMember(12)] public TimeSpan HoistMoveTimeMean { get; set; }
    [ProtoMember(13)] public TimeSpan HoistMoveTimeStd { get; set; }
    [ProtoMember(14)] public TimeSpan DueTimeMean { get; set; }
    [ProtoMember(15)] public TimeSpan DueTimeStd { get; set; }
    [ProtoMember(16)] public TimeSpan DueTimeMin { get; set; }
    [ProtoMember(17)] public int Seed { get; set; }
    [ProtoMember(18)] public double ReadyFactorMin { get; set; }
    [ProtoMember(19)] public double ReadyFactorMax { get; set; }
    [ProtoMember(20)] public TimeSpan ArrivalTimeMean { get; set; }
    [ProtoMember(21)] public TimeSpan ArrivalTimeStd { get; set; }
    [ProtoMember(22)] public TimeSpan HandoverTimeMean { get; set; }
    [ProtoMember(23)] public TimeSpan HandoverTimeStd { get; set; }
    [ProtoMember(24)] public int InitialNumberOfBlocks { get; set; }
  }
}
