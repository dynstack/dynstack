using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DynStack.DataModel.Common;
using ProtoBuf;

namespace DynStack.DataModel.HS {
  public class DatamodelExporter {
    public static string GetDatamodel() {
      return Serializer.GetProto<World>(ProtoBuf.Meta.ProtoSyntax.Proto3);
    }
  }

  public interface IStackingLocation {
    int Height { get; }
    int FreeHeight { get; }
    Block Pickup();
    void Drop(Block b);
  }

  [ProtoContract]
  public class World : IWorld, ISerializable {
    [ProtoMember(1)] public TimeStamp Now { get; set; }
    [ProtoMember(2)] public Stack Production { get; set; }
    [ProtoMember(3)] public List<Stack> Buffers { get; set; }
    [ProtoMember(4)] public Handover Handover { get; set; }
    [ProtoMember(5)] public Crane Crane { get; set; }
    [ProtoMember(6)] public Performance KPIs { get; set; }
    [ProtoMember(7)] public Uncertainties ObservationData { get; set; }
    [ProtoMember(8)] public List<CraneMove> InvalidMoves { get; set; }
    public long PolicyTime { get; set; } = 0;

    public IEnumerable<Block> BlocksInSystem() {
      return Production.BottomToTop.Concat(Buffers.SelectMany(x => x.BottomToTop)).Concat(new[] { Handover.Block, Crane.Load }).Where(x => x != null && !x.Delivered);
    }

    public override string ToString() {
      var sb = new StringBuilder();
      if (Crane.LocationId == 0) sb.AppendLine(Crane.ToString());
      sb.AppendLine(Production.ToString());
      foreach (var b in Buffers.OrderBy(x => x.Id)) {
        if (Crane.LocationId == b.Id) sb.AppendLine(Crane.ToString());
        sb.AppendLine(b.ToString());
      }
      if (Crane.LocationId == Handover.Id) sb.AppendLine(Crane.ToString());
      sb.AppendLine(Handover.ToString());
      sb.AppendLine(KPIs.ToString());
      return sb.ToString();
    }

    #region IWorld members
    IPerformance IWorld.KPIs => KPIs;
    #endregion
  }

  [ProtoContract]
  public class Performance : IPerformance, IComparable<Performance> {
    [ProtoMember(1)] public int CraneManipulations { get; set; }
    [ProtoMember(2)] public double ServiceLevelMean { get; set; }
    [ProtoMember(3)] public double LeadTimeMean { get; set; }
    [ProtoMember(4)] public int DeliveredBlocks { get; set; }
    [ProtoMember(5)] public int TotalBlocksOnTime { get; set; }
    [ProtoMember(6)] public double BlockedArrivalTime { get; set; }
    [ProtoMember(7)] public double TardinessMean { get; set; }
    [ProtoMember(8)] public double BufferUtilizationMean { get; set; }
    [ProtoMember(9)] public double CraneUtilizationMean { get; set; }
    [ProtoMember(10)] public double HandoverUtilizationMean { get; set; }
    [ProtoMember(11)] public double UpstreamUtilizationMean { get; set; }

    public static string[] ObjectiveNames => new[] { "Blocked", "On Time", "Moves" };
    public static bool[] Maximization => new[] { false, true, false };

    public object[] ObjectiveValues => new object[] { BlockedArrivalTime, TotalBlocksOnTime, CraneManipulations };

    public Performance() { }

    public override string ToString() {
      var sb = new StringBuilder();
      sb.Append("Manipulations: ");
      sb.AppendLine(CraneManipulations.ToString());
      sb.Append("Service Level: ");
      sb.AppendLine(ServiceLevelMean.ToString());
      sb.Append("Lead Time: ");
      sb.AppendLine(LeadTimeMean.ToString());
      sb.Append("Delivered Blocks: ");
      sb.AppendLine(DeliveredBlocks.ToString());
      sb.Append("Total Blocks on Time: ");
      sb.AppendLine(TotalBlocksOnTime.ToString());
      sb.Append("Blocked Arrival Time: ");
      sb.AppendLine(BlockedArrivalTime.ToString());
      sb.Append("Tardiness: ");
      sb.AppendLine(TardinessMean.ToString());
      sb.Append("Buffer Utilization: ");
      sb.AppendLine(BufferUtilizationMean.ToString());
      sb.Append("Crane Utilization: ");
      sb.AppendLine(CraneUtilizationMean.ToString());
      sb.Append("Handover Utilization: ");
      sb.AppendLine(HandoverUtilizationMean.ToString());
      sb.Append("Upstream Utilization: ");
      sb.AppendLine(UpstreamUtilizationMean.ToString());
      return sb.ToString();
    }

    public int CompareTo(Performance other) {
      if (other == null) return 1;

      var thisStats = (BlockedArrivalTime, -TotalBlocksOnTime, CraneManipulations);
      var otherStats = (other.BlockedArrivalTime, -other.TotalBlocksOnTime, other.CraneManipulations);

      return thisStats.CompareTo(otherStats);
    }

    public int CompareTo(object obj) {
      if (obj is null) return 1;
      if (obj is Performance other) return this.CompareTo(other);
      throw new ArgumentException($"Cannot compare object of type {GetType().FullName} to object of type {obj.GetType().FullName}");
    }
  }

  //[ProtoContract]
  //public class TimeStamp : IComparable<TimeStamp> {
  //  [ProtoMember(1)] public long MilliSeconds { get; set; }

  //  public int CompareTo(TimeStamp other) => MilliSeconds.CompareTo(other.MilliSeconds);
  //  public static bool operator <(TimeStamp b, TimeStamp c) => b.CompareTo(c) < 0;
  //  public static bool operator <=(TimeStamp b, TimeStamp c) => b.CompareTo(c) <= 0;
  //  public static bool operator >(TimeStamp b, TimeStamp c) => b.CompareTo(c) > 0;
  //  public static bool operator >=(TimeStamp b, TimeStamp c) => b.CompareTo(c) >= 0;
  //  public static TimeSpan operator -(TimeStamp b, TimeStamp c) => TimeSpan.FromMilliseconds(b.MilliSeconds - c.MilliSeconds);
  //  public static TimeStamp operator +(TimeStamp b, TimeSpan c) => new TimeStamp { MilliSeconds = b.MilliSeconds + (long)Math.Round(c.TotalMilliseconds) };
  //  public static TimeStamp operator -(TimeStamp b, TimeSpan c) => new TimeStamp { MilliSeconds = b.MilliSeconds - (long)Math.Round(c.TotalMilliseconds) };

  //  public override string ToString() => string.Format("{0:g}", TimeSpan.FromMilliseconds(MilliSeconds));
  //}

  [ProtoContract]
  public class Stack : IStackingLocation {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public int MaxHeight { get; set; }
    [ProtoMember(3)] public List<Block> BottomToTop { get; set; }

    public int Height => BottomToTop?.Count ?? 0;
    public int FreeHeight => MaxHeight - Height;

    public void Drop(Block b) {
      BottomToTop.Add(b);
    }

    public Block Pickup() {
      var block = BottomToTop.Last();
      BottomToTop.RemoveAt(Height - 1);
      return block;
    }

    public override string ToString() {
      // Hint: may be null after deserialization with protobuf
      if (BottomToTop == null || BottomToTop.Count == 0) return Id + ": empty";
      return $"{Id} {BottomToTop.Count}/{MaxHeight} : {string.Join(" ", BottomToTop.Select(x => x.Id.ToString() + (x.Ready ? "!" : "")))}";
    }
  }

  [ProtoContract]
  public class Block {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public TimeStamp Release { get; set; }
    [ProtoMember(3)] public TimeStamp Due { get; set; }
    [ProtoMember(4)] public bool Ready { get; set; }

    public bool Delivered { get; set; }
  }

  [ProtoContract]
  public class Handover : IStackingLocation {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public bool Ready { get; set; }
    [ProtoMember(3)] public Block Block { get; set; }

    public int Height => Block == null ? 0 : 1;

    public int FreeHeight => 1 - Height;

    public void Drop(Block b) {
      Block = b;
      Ready = false;
    }

    public Block Pickup() {
      throw new InvalidOperationException($"Cannot pick up block from handover {Id}.");
    }

    public override string ToString() {
      if (Block != null) return Id + ": " + Block.Id + (!Block.Ready ? "???" : "!");
      return Id + ": " + (Ready ? "ready" : "closed");
    }
  }

  [ProtoContract]
  public class Crane {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public int LocationId { get; set; }
    [ProtoMember(3)] public Block Load { get; set; }
    [ProtoMember(4)] public CraneSchedule Schedule { get; set; }
    [ProtoMember(5)] public double GirderPosition { get; set; }
    [ProtoMember(6)] public double HoistPosition { get; set; }

    public override string ToString() {
      return $"Crane {Id}: (@ {GirderPosition:0.00},{HoistPosition:0.00}) " + ((Load == null) ? "empty" : (Load.Id.ToString() + (Load.Ready ? "!" : "")));
    }
  }

  [ProtoContract]
  public class CraneSchedule : ISerializable {
    [ProtoMember(1)] public List<CraneMove> Moves { get; set; }
    [ProtoMember(2)] public int SequenceNr { get; set; }

    public override string ToString() {
      if (Moves == null) return "";
      return string.Join(", ", Moves.Select(m => $"{m.Sequence}: [{m.BlockId}] {m.SourceId}->{m.TargetId}"));
    }
  }

  [ProtoContract]
  public class CraneMove {
    [ProtoMember(1)] public int BlockId { get; set; }
    [ProtoMember(2)] public int SourceId { get; set; }
    [ProtoMember(3)] public int TargetId { get; set; }
    [ProtoMember(4)] public int Sequence { get; set; }
    [ProtoMember(5)] public bool EmptyMove { get; set; }

    public override string ToString() {
      return EmptyMove ? $"{Sequence} *->{TargetId}" : $"{Sequence}: {BlockId} {SourceId}->{TargetId}";
    }
  }

  [ProtoContract]
  public class Uncertainties {
    [ProtoMember(1)] public LinkedList<double> ArrivalIntervals { get; set; }
    [ProtoMember(2)] public LinkedList<double> CraneMoveTimes { get; set; }
    [ProtoMember(3)] public LinkedList<double> HandoverReadyIntervals { get; set; }

    public Uncertainties() {
      ArrivalIntervals = new LinkedList<double>();
      CraneMoveTimes = new LinkedList<double>();
      HandoverReadyIntervals = new LinkedList<double>();
    }
  }
}
