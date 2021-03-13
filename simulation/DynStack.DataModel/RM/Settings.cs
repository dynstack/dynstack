using System;
using System.Collections.Generic;
using DynStack.DataModel.Common;
using ProtoBuf;

namespace DynStack.DataModel.RM {
  [ProtoContract]
  public class Settings : ISettings {
    [ProtoMember(1)] public int Height { get; set; }
    [ProtoMember(2)] public double Width { get; set; }
    [ProtoMember(3)] public int MaxHeightForArrival { get; set; }
    [ProtoMember(4)] public int MaxHeightForSortedBuffer { get; set; }
    [ProtoMember(5)] public int MaxHeightForShuffleBuffer { get; set; }
    [ProtoMember(6)] public int MaxHeightAtHandover { get; set; }
    [ProtoMember(7)] public List<double> ArrivalStackPositions { get; set; }
    [ProtoMember(8)] public List<double> ShuffleStackPositions { get; set; }
    [ProtoMember(9)] public List<double> SortedStackPositions { get; set; }
    [ProtoMember(10)] public Dictionary<MillTypes, double> HandoverStackPositions { get; set; }

    [ProtoMember(11)] public int CraneCapacity { get; set; }

    [ProtoMember(12)] public TimeSpan SimulationDuration { get; set; }
    //[ProtoMember(13)] public TimeSpan InterarrivalTimeMean { get; set; }

    [ProtoMember(14)] public TimeSpan CraneMoveTimeMean { get; set; }
    [ProtoMember(15)] public TimeSpan CraneMoveTimeStd { get; set; }
    [ProtoMember(16)] public TimeSpan HoistMoveTimeMean { get; set; }
    [ProtoMember(17)] public TimeSpan HoistMoveTimeStd { get; set; }
    [ProtoMember(18)] public TimeSpan CraneManipulationTimeMean { get; set; }
    [ProtoMember(19)] public TimeSpan CraneManipulationTimeStd { get; set; }
    //[ProtoMember(20)] public TimeSpan DueTimeIntervalMin { get; set; }
    //[ProtoMember(21)] public TimeSpan DueTimeIntervalMean { get; set; }
    //[ProtoMember(22)] public TimeSpan DueTimeIntervalMax { get; set; }

    [ProtoMember(23)] public int Seed { get; set; }
    [ProtoMember(24)] public int ArrivalSequencePoolSize { get; set; }
    [ProtoMember(25)] public TimeSpan ArrivalTimeMean { get; set; }
    [ProtoMember(26)] public TimeSpan ArrivalTimeStd { get; set; }
    //[ProtoMember(27)] public int InitialNumberOfBlocks { get; set; }
    [ProtoMember(28)] public double SafetyDistance { get; set; }

    [ProtoMember(29)] public int ProgramSizeMin { get; set; }
    [ProtoMember(30)] public int ProgramSizeMax { get; set; }
    [ProtoMember(31)] public TimeSpan ProgramBlockIntervalMin { get; set; }
    [ProtoMember(32)] public TimeSpan ProgramBlockIntervalMax { get; set; }
    [ProtoMember(33)] public TimeSpan ProgramIntervalMin { get; set; }
    [ProtoMember(34)] public TimeSpan ProgramIntervalMax { get; set; }
    [ProtoMember(35)] public TimeSpan InitialPhase { get; set; }
    [ProtoMember(36)] public int ProgramCount { get; set; }
    [ProtoMember(37)] public List<double> ArrivalLotSizeWeights { get; set; }
    [ProtoMember(38)] public double ArrivalMillPurity { get; set; }
    [ProtoMember(39)] public TimeSpan ArrivalUnloadTimeMin { get; set; }
    [ProtoMember(40)] public TimeSpan ArrivalUnloadTimeMax { get; set; }
  }
}
