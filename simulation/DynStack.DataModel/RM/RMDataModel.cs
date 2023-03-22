using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DynStack.DataModel.Common;
using ProtoBuf;
using ProtoBuf.Meta;

namespace DynStack.DataModel.RM {
  public class DatamodelExporter {
    public static string GetDatamodel() {
      return Serializer.GetProto<World>(ProtoBuf.Meta.ProtoSyntax.Proto3);
    }
  }
  [ProtoContract] public enum StackTypes {[ProtoEnum] ArrivalStack, [ProtoEnum] ShuffleBuffer, [ProtoEnum] SortedBuffer, [ProtoEnum] HandoverStack }
  [ProtoContract] public enum MillTypes {[ProtoEnum] A, [ProtoEnum] B }

  [ProtoContract]
  public class World : IWorld, IStackingWorld {
    [ProtoMember(1)] public TimeStamp Now { get; set; }
    [ProtoMember(2)] public int Height { get; set; }
    [ProtoMember(3)] public double Width { get; set; }
    [ProtoMember(4)] public List<Location> Locations { get; set; }
    [ProtoMember(5)] public List<Block> BlocksAtSlabYard { get; set; }
    [ProtoMember(6)] public List<Arrival> ArrivalsFromSlabYard { get; set; }
    [ProtoMember(7)] public PlannedCraneMoves CraneMoves { get; set; }
    [ProtoMember(8)] public List<MoveRequest> MoveRequests { get; set; }
    [ProtoMember(9)] public CraneSchedule CraneSchedule { get; set; }

    [ProtoMember(10)] public Crane ShuffleCrane { get; set; }
    [ProtoMember(11)] public Crane HandoverCrane { get; set; }
    [ProtoMember(12)] public Performance KPIs { get; set; }
    [ProtoMember(13)] public Uncertainties ObservationData { get; set; }

    /// <summary>
    /// All blocks that either reside at a stack or which are carried by a crane.
    /// </summary>
    /// <returns>An enumeration of blocks.</returns>
    public IEnumerable<Block> AllBlocksInSystem() {
      return Locations.Select(x => x.Stack.BottomToTop).Concat(new[] { ShuffleCrane.Load.BottomToTop, HandoverCrane.Load.BottomToTop })
        .SelectMany(x => x);
    }
    /// <summary>
    /// All blacks that are known, includes <see cref="AllBlocksInSystem"/>, <see cref="BlocksAtSlabYard"/>, and
    /// <see cref="ArrivalsFromSlabYard"/>.
    /// </summary>
    /// <returns>An enumeration of blocks.</returns>
    public IEnumerable<Block> AllBlocks() {
      return Locations.Select(x => x.Stack.BottomToTop).Concat(new[] { ShuffleCrane.Load.BottomToTop, HandoverCrane.Load.BottomToTop })
        .Concat(ArrivalsFromSlabYard.Select(x => x.Load.BottomToTop))
        .SelectMany(x => x).Concat(BlocksAtSlabYard);
    }

    public override string ToString() {
      var sb = new StringBuilder();
      bool a = false, b = false;
      sb.AppendLine(Now.ToString());
      foreach (var loc in Locations.OrderBy(x => x.GirderPosition)) {
        if (ShuffleCrane.GirderPosition <= loc.GirderPosition && !a) {
          sb.AppendLine(ShuffleCrane.ToString());
          a = true;
        }
        if (HandoverCrane.GirderPosition <= loc.GirderPosition && !b) {
          sb.AppendLine(HandoverCrane.ToString());
          b = true;
        }
        sb.AppendLine(loc.ToString());
      }
      if (!a) sb.AppendLine(ShuffleCrane.ToString());
      if (!b) sb.AppendLine(HandoverCrane.ToString());
      sb.AppendLine(KPIs.ToString());
      return sb.ToString();
    }

    #region IWorld members
    TimeStamp IStackingWorld.Now => Now;
    int IStackingWorld.Height => Height;
    IEnumerable<ILocation> IStackingWorld.Locations => Locations;
    IEnumerable<IBlock> IStackingWorld.Blocks => AllBlocks();
    IEnumerable<ICrane> IStackingWorld.Cranes => new ICrane[] { ShuffleCrane, HandoverCrane };
    IEnumerable<IMove> IStackingWorld.Moves => CraneMoves.Moves;
    IEnumerable<IMoveRequest> IStackingWorld.MoveRequests => MoveRequests;
    ICraneSchedule IStackingWorld.CraneSchedule => CraneSchedule;
    IPerformance IWorld.KPIs => KPIs;

    public string GetDataModel(ProtoSyntax syntax = ProtoSyntax.Proto3) {
      return RuntimeTypeModel.Default.GetSchema(GetType(), syntax);
    }
    #endregion
  }

  [ProtoContract]
  public class Stack : IStack {
    [ProtoMember(1)] public List<Block> BottomToTop { get; set; }
    public int Size => BottomToTop.Count;

    public Block Topmost => BottomToTop.LastOrDefault();
    public IEnumerable<Block> TopToBottom {
      get {
        for (var i = BottomToTop.Count - 1; i >= 0; i--)
          yield return BottomToTop[i];
      }
    }

    public Stack() {
      BottomToTop = new List<Block>();
    }

    public string ToString(bool horizontal = false) {
      return string.Join(horizontal ? " " : Environment.NewLine, BottomToTop.Reverse<Block>().Select(x => $"({x.Id})"));
    }
    public override string ToString() {
      return ToString(false);
    }

    #region IStack members
    int IStack.Size => Size;
    IEnumerable<IBlock> IStack.BottomToTop => BottomToTop;
    IEnumerable<IBlock> IStack.TopToBottom => TopToBottom;
    void IStack.Clear() => BottomToTop.Clear();
    void IStack.AddOnTop(IBlock block) {
      if (block == null) throw new ArgumentNullException(nameof(block));
      if (block is Block b)
        BottomToTop.Add(b);
      else throw new ArgumentException($"Wrong type of block: Expected {typeof(Block).FullName}, received {block.GetType().FullName}.");
    }
    void IStack.AddOnTop(IStack stack) {
      if (stack == null) throw new ArgumentNullException(nameof(stack));
      BottomToTop.AddRange(stack.BottomToTop.Cast<Block>());
    }
    void IStack.AddToBottom(IBlock block) {
      if (block == null) throw new ArgumentNullException(nameof(block));
      if (block is Block b) {
        if (BottomToTop.Count > 0) BottomToTop.Insert(0, b);
        else BottomToTop.Add(b);
      } else throw new ArgumentException($"Wrong type of block: Expected {typeof(Block).FullName}, received {block.GetType().FullName}.");
    }
    void IStack.AddToBottom(IStack stack) {
      if (stack == null) throw new ArgumentNullException(nameof(stack));
      if (BottomToTop.Count > 0) BottomToTop.InsertRange(0, stack.BottomToTop.Cast<Block>());
      else BottomToTop.AddRange(stack.BottomToTop.Cast<Block>());
    }
    IBlock IStack.RemoveFromTop() {
      var block = BottomToTop[Size - 1];
      BottomToTop.RemoveAt(Size - 1);
      return block;
    }
    IStack IStack.RemoveFromTop(int amount) {
      var split = new Stack();
      for (var i = amount; i > 0; i--)
        split.BottomToTop.Add(BottomToTop[Size - i]);

      BottomToTop.RemoveRange(Size - amount, amount);
      return split;
    }
    IBlock IStack.RemoveFromBottom() {
      var block = BottomToTop[0];
      BottomToTop.RemoveAt(0);
      return block;
    }
    IStack IStack.RemoveFromBottom(int amount) {
      var split = new Stack();
      for (var i = 0; i < amount; i++)
        split.BottomToTop.Add(BottomToTop[i]);

      BottomToTop.RemoveRange(0, amount);
      return split;
    }
    #endregion
  }

  [ProtoContract]
  public class Location : ILocation {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public double GirderPosition { get; set; }
    [ProtoMember(3)] public int MaxHeight { get; set; }
    [ProtoMember(4)] public Stack Stack { get; set; }
    [ProtoMember(5)] public StackTypes Type { get; set; }
    [ProtoMember(6)] public MillTypes? MillType { get; set; }

    public Block Topmost => Stack.Topmost;
    public int FreeHeight => MaxHeight - Stack.Size;
    public int Height => Stack.Size;

    public void Dropoff(Block block) {
      if (FreeHeight < 1) throw new InvalidOperationException("Stack is full!");
      Stack.BottomToTop.Add(block);
    }

    public void Dropoff(Stack stack) {
      if (FreeHeight < stack.Size) throw new InvalidOperationException("Stack is full!");
      Stack.BottomToTop.AddRange(stack.BottomToTop);
    }

    public Block Pickup() {
      if (Height < 1) throw new InvalidOperationException("No block at stack!");
      var block = Stack.BottomToTop[Height - 1];
      Stack.BottomToTop.RemoveAt(Height - 1);
      return block;
    }

    public Stack Pickup(int amt) {
      if (Height < amt) throw new InvalidOperationException("Not enough blocks at stack!");
      var split = new Stack();
      for (var i = amt; i > 0; i--) {
        split.BottomToTop.Add(Stack.BottomToTop[Height - i]);
      }
      Stack.BottomToTop.RemoveRange(Height - amt, amt);
      return split;
    }

    public override string ToString() {
      if (Stack.BottomToTop.Count == 0) return $"{Type.ToString().Substring(0, 2)} {Id} 0/{MaxHeight}";
      return $"{Type.ToString().Substring(0, 2)} {Id} {Stack.BottomToTop.Count}/{MaxHeight} : {string.Join(" ", Stack.BottomToTop.Select(x => x.ToString()))}";
    }

    #region ILocation members
    int ILocation.Id => Id;
    int ILocation.MaxHeight => MaxHeight;
    int ILocation.FreeHeight => FreeHeight;
    IStack ILocation.Stack => Stack;
    IBlock ILocation.Topmost => Topmost;
    double ILocation.GirderPosition => GirderPosition;

    IBlock ILocation.Pickup() {
      return Pickup();
    }

    IStack ILocation.Pickup(int amt) {
      return Pickup(amt);
    }

    void ILocation.Dropoff(IBlock block) {
      if (block == null) throw new ArgumentNullException(nameof(block));
      if (block is Block b)
        Dropoff(b);
      else throw new ArgumentException($"Wrong type of block: Expected {typeof(Block).FullName}, received {block.GetType().FullName}.");
    }

    void ILocation.Dropoff(IStack stack) {
      if (stack == null) throw new ArgumentNullException(nameof(stack));
      if (stack is Stack s)
        Dropoff(s);
      else throw new ArgumentException($"Wrong type of stack: Expected {typeof(Stack).FullName}, received {stack.GetType().FullName}.");
    }
    #endregion
  }

  [ProtoContract]
  public class Block : IBlock {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public int Sequence { get; set; }
    [ProtoMember(3)] public MillTypes Type { get; set; }
    [ProtoMember(4)] public int ProgramId { get; set; }
    [ProtoMember(5)] public TimeStamp? Arrived { get; set; }
    [ProtoMember(6)] public bool Rolled { get; set; }

    #region IBlock members
    int IBlock.Id => Id;
    #endregion

    public override string ToString() {
      return $"({Id};{ProgramId};{Type};{Sequence})";
    }
  }

  [ProtoContract]
  public class Arrival {
    [ProtoMember(1)] public int Vehicle { get; set; }
    [ProtoMember(2)] public Stack Load { get; set; }
    [ProtoMember(3)] public TimeStamp ArrivalEstimate { get; set; }
  }
  [ProtoContract]
  public class Crane : ICrane {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public Stack Load { get; set; }
    [ProtoMember(3)] public double GirderPosition { get; set; }
    [ProtoMember(4)] public double HoistLevel { get; set; }
    [ProtoMember(5)] public int CraneCapacity { get; set; }
    [ProtoMember(6)] public double Width { get; set; }
    [ProtoMember(7)] public double MinPosition { get; set; }
    [ProtoMember(8)] public double MaxPosition { get; set; }

    public Crane() {
      Load = new Stack();
    }

    public bool CanReach(double girderPosition) {
      return MinPosition <= girderPosition && girderPosition <= MaxPosition;
    }

    public override string ToString() {
      if (Load.Size == 0) return $"Crane {Id}";
      return $"Crane {Id}: {string.Join(" ", Load.BottomToTop.Select(x => x.ToString()))}";
    }

    #region ICrane members
    int ICrane.Id => Id;
    int ICrane.Capacity => CraneCapacity;
    double ICrane.Width => Width;
    IStack ICrane.Load => Load;
    double ICrane.GirderPosition { get => GirderPosition; set => GirderPosition = value; }
    double ICrane.HoistLevel { get => HoistLevel; set => HoistLevel = value; }
    bool ICrane.CanReach(double girderPosition) => CanReach(girderPosition);
    #endregion
  }

  [ProtoContract]
  public class CraneSchedule : ICraneSchedule {
    [ProtoMember(1)] public int ScheduleNr { get; set; }
    private List<CraneScheduleActivity> _activities;
    [ProtoMember(2)]
    public List<CraneScheduleActivity> Activities {
      get => _activities;
      set {
        if (value == null) _activities = new List<CraneScheduleActivity>();
        else _activities = value;
      }
    }

    public CraneSchedule() {
      _activities = new List<CraneScheduleActivity>();
    }


    #region ICraneSchedule members
    int ICraneSchedule.ScheduleNr => ScheduleNr;
    int ICraneSchedule.Tasks => _activities.Count;

    IEnumerable<(int index, int moveId, int craneId, int priority, CraneScheduleActivityState state)> ICraneSchedule.TaskSequence =>
      _activities.OrderBy(x => x.Priority).Select((v, i) => (i, v.MoveId, v.CraneId, v.Priority, v.State));

    int ICraneSchedule.Add(int moveId, int craneId, int priority, CraneScheduleActivityState state) {
      if (((ICraneSchedule)this).ContainsMove(moveId)) throw new ArgumentException($"{moveId} already contained in schedule.", nameof(moveId));
      _activities.Add(new CraneScheduleActivity() { MoveId = moveId, CraneId = craneId, Priority = priority, State = state });
      return _activities.Count - 1;
    }
    void ICraneSchedule.Insert(int index, int moveId, int craneId, int priority, CraneScheduleActivityState state) {
      if (index == _activities.Count) ((ICraneSchedule)this).Add(moveId, craneId, priority, state);
      else {
        if (((ICraneSchedule)this).ContainsMove(moveId)) throw new ArgumentException($"{moveId} already contained in schedule.", nameof(moveId));
        _activities.Insert(index, new CraneScheduleActivity() { MoveId = moveId, CraneId = craneId, Priority = priority, State = state });
      }
    }

    void ICraneSchedule.Remove(int moveId) {
      for (var i = 0; i < _activities.Count; i++) {
        if (_activities[i].MoveId == moveId) {
          _activities.RemoveAt(i);
          break;
        }
      }
    }

    void ICraneSchedule.UpdateState(int moveId, CraneScheduleActivityState newState) {
      var activity = _activities.Single(x => x.MoveId == moveId);
      activity.State = newState;
    }

    void ICraneSchedule.UpdateCrane(int moveId, int craneId) {
      var activity = _activities.Single(x => x.MoveId == moveId);
      activity.CraneId = craneId;
    }

    void ICraneSchedule.Clear() {
      _activities.Clear();
    }

    bool ICraneSchedule.ContainsMove(int moveId) {
      return _activities.Any(x => x.MoveId == moveId);
    }
    #endregion
  }

  [ProtoContract]
  public class CraneScheduleActivity {
    [ProtoMember(1)] public int MoveId { get; set; }
    [ProtoMember(2)] public int CraneId { get; set; }
    [ProtoMember(3)] public int Priority { get; set; }
    [ProtoMember(4)] public CraneScheduleActivityState State { get; set; }
  }

  [ProtoContract]
  public class CraneMove : IMove {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public MoveType Type { get; set; }
    [ProtoMember(3)] public int PickupLocationId { get; set; }
    [ProtoMember(4)] public int DropoffLocationId { get; set; }
    public double PickupGirderPosition { get; set; }
    public double DropoffGirderPosition { get; set; }
    [ProtoMember(7)] public int Amount { get; set; }
    [ProtoMember(8)] public TimeStamp ReleaseTime { get; set; }
    [ProtoMember(9)] public TimeStamp DueDate { get; set; }
    [ProtoMember(10)] public int? RequiredCraneId { get; set; }
    private HashSet<int> _predecessorIds = new HashSet<int>();
    [ProtoMember(11)]
    private List<int> ProtobufPredecessorIds {
      get => _predecessorIds.ToList();
      set {
        if (value == null) _predecessorIds = new HashSet<int>();
        else _predecessorIds = new HashSet<int>(value);
      }
    }
    public HashSet<int> PredecessorIds {
      get => _predecessorIds;
      set {
        if (value == null) _predecessorIds = new HashSet<int>();
        else _predecessorIds = value;
      }
    }
    private List<int> _movedBlockIds = new List<int>();
    [ProtoMember(12)]
    private List<int> ProtobufMovedBlockIds {
      get => _movedBlockIds.ToList();
      set {
        if (value == null) _movedBlockIds = new List<int>();
        else _movedBlockIds = new List<int>(value);
      }
    }
    public List<int> MovedBlockIds {
      get => _movedBlockIds;
      set {
        if (value == null) _movedBlockIds = new List<int>();
        else _movedBlockIds = value;
      }
    }

    public int Predecessors => PredecessorIds.Count;


    public void IsFinished(int moveId) {
      _predecessorIds.Remove(moveId);
    }

    public override string ToString() {
      switch (Type) {
        case MoveType.MoveToPickup:
          return $"{Id} MoveTo {PickupGirderPosition}";
        case MoveType.PickupAndDropoff:
          return $"{Id} Pickup {Amount} from {PickupLocationId} and dropoff at {DropoffLocationId}";
      }
      return $"{Id} Unknown Type ({Type})";
    }

    #region IMove members
    int IMove.Id => Id;
    MoveType IMove.Type => Type;
    int IMove.PickupLocationId => PickupLocationId;
    double IMove.PickupGirderPosition => PickupGirderPosition;
    int IMove.DropoffLocationId => DropoffLocationId;
    double IMove.DropoffGirderPosition => DropoffGirderPosition;
    int IMove.Amount => Amount;
    TimeStamp IMove.ReleaseTime => ReleaseTime;
    TimeStamp IMove.DueDate => DueDate;
    int? IMove.RequiredCraneId => RequiredCraneId;
    ISet<int> IMove.PredecessorIds => PredecessorIds;
    int IMove.Predecessors => Predecessors;
    IList<int> IMove.MovedBlockIds => MovedBlockIds;

    void IMove.RemoveFromPredecessors(int pred) => IsFinished(pred);
    #endregion
  }

  [ProtoContract]
  public class PlannedCraneMoves {
    [ProtoMember(1)] public int SequenceNr { get; set; }
    [ProtoMember(2)] public List<CraneMove> Moves { get; set; }
    public PlannedCraneMoves() {
      Moves = new List<CraneMove>();
    }
    public int Count => Moves?.Count??0;
  }

  [ProtoContract]
  public class MoveRequest : IMoveRequest {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public int TargetLocationId { get; set; }
    [ProtoMember(3)] public int BlockId { get; set; }
    [ProtoMember(4)] public TimeStamp DueDate { get; set; }

    #region IMoveRequest members
    int IMoveRequest.Id => Id;
    int IMoveRequest.TargetLocationId => TargetLocationId;
    int IMoveRequest.BlockId => BlockId;
    TimeStamp IMoveRequest.DueDate => DueDate;
    #endregion
  }

  [ProtoContract]
  public class Performance : IPerformance, IComparable<Performance> {
    [ProtoMember(1)] public int CraneManipulations { get; set; }
    [ProtoMember(2)] public double ServiceLevelMean { get; set; }
    [ProtoMember(3)] public double LeadTimeMean { get; set; }
    [ProtoMember(4)] public int DeliveredBlocks { get; set; }
    [ProtoMember(5)] public int TotalBlocksOnTime { get; set; }
    [ProtoMember(6)] public double TardinessMean { get; set; }
    [ProtoMember(7)] public double ShuffleBufferUtilizationMean { get; set; }
    [ProtoMember(8)] public double SortedBufferUtilizationMean { get; set; }
    [ProtoMember(9)] public double ShuffleCraneUtilizationMean { get; set; }
    [ProtoMember(10)] public double HandoverCraneUtilizationMean { get; set; }
    [ProtoMember(11)] public double MillAUtilizationMean { get; set; }
    [ProtoMember(12)] public double MillBUtilizationMean { get; set; }
    [ProtoMember(13)] public int RollingProgramMessups { get; set; }
    [ProtoMember(14)] public double BlockedMillTime { get; set; }

    public static string[] ObjectiveNames => new[] { "Messups", "Blocked", "On Time", "Moves" };
    public static bool[] Maximization => new[] { false, false, true, false };

    public object[] ObjectiveValues => new object[] { RollingProgramMessups, BlockedMillTime, TotalBlocksOnTime, CraneManipulations };

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
      sb.Append("Tardiness: ");
      sb.AppendLine(TardinessMean.ToString());
      sb.Append("Buffer (shuffle) Utilization: ");
      sb.AppendLine(ShuffleBufferUtilizationMean.ToString());
      sb.Append("Buffer (sorted) Utilization: ");
      sb.AppendLine(SortedBufferUtilizationMean.ToString());
      sb.Append("Crane (shuffle) Utilization: ");
      sb.AppendLine(ShuffleCraneUtilizationMean.ToString());
      sb.Append("Crane (handover) Utilization: ");
      sb.AppendLine(HandoverCraneUtilizationMean.ToString());
      sb.Append("Mill (A) Utilization: ");
      sb.AppendLine(MillAUtilizationMean.ToString());
      sb.Append("Mill (B) Utilization: ");
      sb.AppendLine(MillBUtilizationMean.ToString());
      sb.Append("Rolling program mess ups: ");
      sb.AppendLine(RollingProgramMessups.ToString());
      sb.Append("Blocked mill time: ");
      sb.AppendLine(BlockedMillTime.ToString());
      return sb.ToString();
    }

    public string GetObjectivesString() {
      return string.Join(" / ",
        $"F1↓: {RollingProgramMessups}",
        $"F2↓: {BlockedMillTime:0.00}",
        $"F2↑: {TotalBlocksOnTime}",
        $"F3↓: {CraneManipulations}"
      );
    }

    public int CompareTo(Performance other) {
      if (other == null) return 1;

      var thisStats = (RollingProgramMessups, BlockedMillTime, -TotalBlocksOnTime, CraneManipulations);
      var otherStats = (other.RollingProgramMessups, other.BlockedMillTime, -other.TotalBlocksOnTime, other.CraneManipulations);

      return thisStats.CompareTo(otherStats);
    }

    public int CompareTo(object obj) {
      if (obj is null) return 1;
      if (obj is Performance other) return this.CompareTo(other);
      throw new ArgumentException($"Cannot compare object of type {GetType().FullName} to object of type {obj.GetType().FullName}");
    }
  }

  [ProtoContract]
  public class Uncertainties {
    [ProtoMember(1)] public LinkedList<double> ArrivalIntervals { get; set; }
    [ProtoMember(2)] public LinkedList<double> CraneMoveTimes { get; set; }
    [ProtoMember(3)] public LinkedList<double> MillBlockIntervals { get; set; }

    public Uncertainties() {
      ArrivalIntervals = new LinkedList<double>();
      CraneMoveTimes = new LinkedList<double>();
      MillBlockIntervals = new LinkedList<double>();
    }
  }
}
