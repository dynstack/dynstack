using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ProtoBuf;
using ProtoBuf.Meta;

namespace DynStack.DataModel.CS {
  [ProtoContract]
  public class World : IStackingWorld {
    [ProtoMember(1)] public TimeStamp Now { get; set; }
    [ProtoMember(2)] public int Height { get; set; }
    [ProtoMember(3)] public double Width { get; set; }
    [ProtoMember(4)] public List<Location> Locations { get; set; }
    [ProtoMember(5)] public List<CraneMove> CraneMoves { get; set; } = new List<CraneMove>();
    [ProtoMember(6)] public List<Crane> Cranes { get; set; }
    [ProtoMember(7)] public List<MoveRequest> MoveRequests { get; set; }
    [ProtoMember(8)] public CraneSchedule CraneSchedule { get; set; }

    /// <summary>
    /// All blocks that either reside at a stack or which are carried by a crane.
    /// </summary>
    /// <returns>An enumeration of blocks.</returns>
    public IEnumerable<Block> AllBlocks() {
      return Locations.Select(x => x.Stack.BottomToTop).Concat(Cranes.Select(x => x.Load.BottomToTop))
        .SelectMany(x => x);
    }

    public override string ToString() {
      var sb = new StringBuilder();
      var craneOrder = Cranes.OrderBy(x => x.GirderPosition).GetEnumerator();
      var moreCranes = craneOrder.MoveNext();

      foreach (var stack in Locations.OrderBy(x => x.GirderPosition)) {
        while (moreCranes && craneOrder.Current.GirderPosition < stack.GirderPosition) {
          sb.AppendLine(craneOrder.Current.ToString());
          moreCranes = craneOrder.MoveNext();
        }
        sb.AppendLine(stack.ToString());
      }
      while (moreCranes) {
        sb.AppendLine(craneOrder.Current.ToString());
        moreCranes = craneOrder.MoveNext();
      }
      return sb.ToString();
    }

    #region IStackingWorld members
    TimeStamp IStackingWorld.Now => Now;
    int IStackingWorld.Height => Height;
    IEnumerable<ILocation> IStackingWorld.Locations => Locations;
    IEnumerable<IBlock> IStackingWorld.Blocks => AllBlocks();
    IEnumerable<ICrane> IStackingWorld.Cranes => Cranes;
    IEnumerable<IMove> IStackingWorld.Moves => CraneMoves;
    IEnumerable<IMoveRequest> IStackingWorld.MoveRequests => MoveRequests;
    ICraneSchedule IStackingWorld.CraneSchedule => CraneSchedule;

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
      return string.Join(horizontal ? " " : Environment.NewLine, BottomToTop.Reverse<Block>().Select(x => x.Id.ToString()));
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

    public Block Topmost => Stack.Topmost;
    public int FreeHeight => MaxHeight - Stack.Size;
    public int Height => Stack.Size;

    public Location() {
      Stack = new Stack();
    }

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
      if (Stack.BottomToTop.Count == 0) return Id + ": empty";
      return $"{Id} {Stack.BottomToTop.Count}/{MaxHeight} : {string.Join(" ", Stack.BottomToTop.Select(x => x.Id.ToString()))}";
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

    #region IBlock members
    int IBlock.Id => Id;
    #endregion
  }

  [ProtoContract]
  public class Arrival {
    [ProtoMember(1)] public int Vehicle { get; set; }
    [ProtoMember(2)] public Stack Load { get; set; }
    [ProtoMember(3)] public TimeStamp ArrivalDate { get; set; }
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

    IEnumerable<(int index, int moveId, int craneId, int priority)> ICraneSchedule.TaskSequence =>
      _activities.OrderBy(x => x.Priority).Select((v, i) => (i, v.MoveId, v.CraneId, v.Priority));

    int ICraneSchedule.Add(int moveId, int craneId, int priority) {
      if (((ICraneSchedule)this).ContainsMove(moveId)) throw new ArgumentException($"{moveId} already contained in schedule.", nameof(moveId));
      _activities.Add(new CraneScheduleActivity() { MoveId = moveId, CraneId = craneId, Priority = priority });
      return _activities.Count - 1;
    }

    void ICraneSchedule.Remove(int moveId) {
      for (var i = 0; i < _activities.Count; i++) {
        if (_activities[i].MoveId == moveId) {
          _activities.RemoveAt(i);
          break;
        }
      }
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
  }

  [ProtoContract]
  public class CraneMove : IMove {
    [ProtoMember(1)] public int Id { get; set; }
    [ProtoMember(2)] public MoveType Type { get; set; }
    [ProtoMember(3)] public int PickupLocationId { get; set; }
    [ProtoMember(4)] public double PickupGirderPosition { get; set; }
    [ProtoMember(5)] public int DropoffLocationId { get; set; }
    [ProtoMember(6)] public double DropoffGirderPosition { get; set; }
    [ProtoMember(7)] public int Amount { get; set; }
    [ProtoMember(8)] public TimeStamp ReleaseTime { get; set; }
    [ProtoMember(9)] public TimeStamp DueDate { get; set; }
    [ProtoMember(10)] public int? RequiredCraneId { get; set; }
    [ProtoMember(11)] public int? DirectSuccessorId { get; set; }
    private HashSet<int> _predecessorIds = new HashSet<int>();
    [ProtoMember(12)]
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

    void IMove.RemoveFromPredecessors(int pred) => IsFinished(pred);
    #endregion
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
}
