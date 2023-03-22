using System;
using System.Collections.Generic;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {

  public interface ICraneMoveEvent {
    int Id { get; }
    int PickupLocation { get; }
    double PickupGirderPosition { get; }
    int DropoffLocation { get; }
    double DropoffGirderPosition { get; }
    int Amount { get; }
    MoveType Type { get; }
    TimeStamp ReleaseTime { get; }
    TimeStamp DueDate { get; }
    int? RequiredCraneId { get; }
    ISet<int> PredecessorIds { get; }
    int Predecessors { get; }
    bool RaiseHoistAfterService { get; }
    ICraneAgent Assigned { get; set; }
    IList<int> MovedBlocks { get; }

    Event Started { get; }
    Event Finished { get; }

    void RemoveFromPredecessors(int moveId);
  }

  public class CraneMoveEvent : ICraneMoveEvent {
    private IMove _move;

    public int Id => _move.Id;
    public int PickupLocation => _move.PickupLocationId;
    public double PickupGirderPosition => _move.PickupGirderPosition;
    public int DropoffLocation => _move.DropoffLocationId;
    public double DropoffGirderPosition => _move.DropoffGirderPosition;
    public int Amount => _move.Amount;
    public MoveType Type => _move.Type;
    public TimeStamp ReleaseTime => _move.ReleaseTime;
    public TimeStamp DueDate => _move.DueDate;
    public int? RequiredCraneId => _move.RequiredCraneId;
    public ISet<int> PredecessorIds => _move.PredecessorIds;
    public int Predecessors => _move.Predecessors;
    public IList<int> MovedBlocks => _move.MovedBlockIds;

    public bool RaiseHoistAfterService { get; private set; }

    private ICraneAgent _assigned;
    public ICraneAgent Assigned {
      get => _assigned;
      set {
        if (value != null && RequiredCraneId.HasValue && RequiredCraneId.Value != value.Id)
          throw new InvalidOperationException($"Cannot assign move to crane {value.Id}, due to requirement for crane {RequiredCraneId.Value}.");
        else _assigned = value;
      }
    }

    public Event Started { get; private set; }
    public Event Finished { get; private set; }

    public CraneMoveEvent(SimSharp.Simulation environment, IMove move, bool raiseHoistAfterService = false) {
      _move = move;
      RaiseHoistAfterService = raiseHoistAfterService;

      Started = new Event(environment);
      Finished = new Event(environment);
    }

    public void RemoveFromPredecessors(int moveId) => _move.RemoveFromPredecessors(moveId);
  }
}
