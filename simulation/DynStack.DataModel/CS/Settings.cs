using System;
using System.Collections.Generic;
using DynStack.DataModel.Common;
using ProtoBuf;

namespace DynStack.DataModel.CS {
  [ProtoContract]
  public class Settings : ISettings {
    [ProtoMember(1)] public int Seed { get; set; }
    [ProtoMember(2)] public TimeSpan SimulationDuration { get; set; }

    [ProtoMember(3)] public int Height { get; set; }
    [ProtoMember(4)] public double Width { get; set; }

    [ProtoMember(5)] public int MaxHeightForArrival { get; set; }
    [ProtoMember(6)] public int MaxHeightForBuffer { get; set; }
    [ProtoMember(7)] public int MaxHeightForHandover { get; set; }

    [ProtoMember(8)] public List<double> ArrivalStackPositions { get; set; }
    [ProtoMember(9)] public List<double> BufferStackPositions { get; set; }
    [ProtoMember(10)] public List<double> HandoverStackPositions { get; set; }

    [ProtoMember(11)] public int BlockClasses { get; set; }
    [ProtoMember(12)] public List<int> BufferStackClasses { get; set; }

    [ProtoMember(13)] public double SafetyDistance { get; set; }

    [ProtoMember(14)] public TimeSpan CraneMoveTimeMean { get; set; }
    [ProtoMember(15)] public TimeSpan CraneMoveTimeStd { get; set; }
    [ProtoMember(16)] public TimeSpan HoistMoveTimeMean { get; set; }
    [ProtoMember(17)] public TimeSpan HoistMoveTimeStd { get; set; }
    [ProtoMember(18)] public TimeSpan CraneManipulationTimeMean { get; set; }
    [ProtoMember(19)] public TimeSpan CraneManipulationTimeStd { get; set; }

    [ProtoMember(20)] public TimeSpan ArrivalTimeMean { get; set; }
    [ProtoMember(21)] public TimeSpan ArrivalTimeStd { get; set; }
    [ProtoMember(22)] public double ArrivalCountMean { get; set; }
    [ProtoMember(23)] public double ArrivalCountStd { get; set; }
    [ProtoMember(24)] public TimeSpan ArrivalServiceTimeMean { get; set; }
    [ProtoMember(25)] public TimeSpan ArrivalServiceTimeStd { get; set; }

    [ProtoMember(26)] public TimeSpan HandoverTimeMean { get; set; }
    [ProtoMember(27)] public TimeSpan HandoverTimeStd { get; set; }
    [ProtoMember(28)] public double HandoverCountMean { get; set; }
    [ProtoMember(29)] public double HandoverCountStd { get; set; }
    [ProtoMember(30)] public TimeSpan HandoverServiceTimeMean { get; set; }
    [ProtoMember(31)] public TimeSpan HandoverServiceTimeStd { get; set; }

    [ProtoMember(32)] public double InitialBufferUtilization { get; set; }
  }
}
