using System.Collections.Generic;

namespace DynStack.DataModel {
  /// <summary>
  /// The schedule contains the list of moves that have to be performed for each crane
  /// </summary>
  public interface ICraneSchedule {
    /// <summary>
    /// This number indicates a priority when there are conflicting schedules, a higher number
    /// indicates a higher priority.
    /// </summary>
    int ScheduleNr { get; }
    /// <summary>
    /// The number of tasks in the schedule, i.e., length of <see cref="TaskSequence"/>.
    /// </summary>
    int Tasks { get; }
    /// <summary>
    /// The main schedule is a sequence of moves, assigned to a crane and given a certain priority.
    /// The priority needs to be non-decreasing per crane.
    /// </summary>
    IEnumerable<(int index, int moveId, int craneId, int priority)> TaskSequence { get; }

    /// <summary>
    /// Add a new move to the end of the sequence.
    /// </summary>
    /// <remarks>
    /// The priority will be ignored and default to one more than the larget priority overall
    /// </remarks>
    /// <param name="moveId">The id of the move that is to be performed.</param>
    /// <param name="craneId">The crane that is to carry out the move.</param>
    /// <returns>The index at which it was added.</returns>
    int Add(int moveId, int craneId, int priority);
    /// <summary>
    /// Remove a move from the sequence (and also the assigned crane)
    /// </summary>
    /// <param name="moveId">The move id to remove</param>
    void Remove(int moveId);

    /// <summary>
    /// Remove all planned moves
    /// </summary>
    void Clear();
    /// <summary>
    /// 
    /// </summary>
    /// <param name="moveId"></param>
    /// <returns></returns>
    bool ContainsMove(int moveId);

  }
}
