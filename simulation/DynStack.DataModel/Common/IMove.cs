using System.Collections.Generic;

namespace DynStack.DataModel {
  /// <summary>
  /// A crane move is a description of work that needs to be performed by a crane.
  /// </summary>
  public interface IMove {
    /// <summary>
    /// The unique identifier of the crane move
    /// </summary>
    int Id { get; }
    /// <summary>
    /// The type of move, can be either to move to a position, to pickup block(s) or to drop them off
    /// </summary>
    MoveType Type { get; }
    /// <summary>
    /// The identifier of the location at which the pickup should occur.
    /// </summary>
    /// <remarks>
    /// This is only used when <see cref="Type"/> is set to PickupAndDropoff.
    /// The property <see cref="PickupGirderPosition"/> must also be set to the corresponding position of that location.
    /// </remarks>
    int PickupLocationId { get; }
    /// <summary>
    /// The actual position where the crane should pick something up.
    /// </summary>
    double PickupGirderPosition { get; }
    /// <summary>
    /// The identifier of the location at which thedropoff should occur.
    /// </summary>
    /// <remarks>
    /// This is only used when <see cref="Type"/> is set to PickupAndDropoff.
    /// The property <see cref="DropoffGirderPosition"/> must also be set to the corresponding position of that location.
    /// </remarks>
    int DropoffLocationId { get; }
    /// <summary>
    /// The actual position to which the crane should drop something off (or move to, <see cref="Type"/>).
    /// </summary>
    double DropoffGirderPosition { get; }
    /// <summary>
    /// The amount of blocks that should be manipulated.
    /// </summary>
    /// <remarks>
    /// This is only used when <see cref="Type"/> is set to PickupAndDropoff.
    /// </remarks>
    int Amount { get; }
    /// <summary>
    /// The block ids that are manipulated, if any.
    /// </summary>
    IList<int> MovedBlockIds { get; }

    /// <summary>
    /// The release time, when this move can be performed at the earliest.
    /// </summary>
    TimeStamp ReleaseTime { get; }
    /// <summary>
    /// The due date, when this move should be finished at the latest.
    /// </summary>
    /// <remarks>
    /// It is difficult and not always possible to guarantee this. It depends on the scheduler implementation and
    /// whether this is actually possible given the positions and movement speeds.
    /// </remarks>
    TimeStamp DueDate { get; }
    /// <summary>
    /// This can be set to limit the move to a certain specific crane.
    /// </summary>
    int? RequiredCraneId { get; }
    /// <summary>
    /// The identifiers of those moves that need to be finished before this can be started.
    /// </summary>
    ISet<int> PredecessorIds { get; }
    /// <summary>
    /// The total number of predecessor
    /// </summary>
    int Predecessors { get; }

    /// <summary>
    /// A method that tells the move that a potential predecessor should be removed.
    /// If the <paramref name="moveId"/> is not a predecessor, nothing happens.
    /// </summary>
    /// <param name="moveId">The identifier of the move that should be removed.</param>
    void RemoveFromPredecessors(int moveId);
  }
}
