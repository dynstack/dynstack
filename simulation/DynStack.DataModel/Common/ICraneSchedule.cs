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
    IEnumerable<(int index, int moveId, int craneId, int priority, CraneScheduleActivityState state)> TaskSequence { get; }

    /// <summary>
    /// Add a new move to the end of the sequence.
    /// </summary>
    /// <param name="moveId">The id of the move that is to be performed.</param>
    /// <param name="craneId">The crane that is to carry out the move.</param>
    /// <param name="priority">The priority of the move, the lowest number (=highest priority) indicates the most prior move.</param>
    /// <param name="state">The state of the move</param>
    /// <returns>The index at which it was added.</returns>
    int Add(int moveId, int craneId, int priority, CraneScheduleActivityState state = CraneScheduleActivityState.Created);
    /// <summary>
    /// Insert a move in a certain position of the sequence.
    /// </summary>
    /// <param name="index">The position in the sequence.</param>
    /// <param name="moveId">The id of the move that is to be performed.</param>
    /// <param name="craneId">The crane that is to carry out the move.</param>
    /// <param name="priority">The priority of the move, the lowest number (=highest priority) indicates the most prior move.</param>
    /// <param name="state">The state of the move</param>
    void Insert(int index, int moveId, int craneId, int priority, CraneScheduleActivityState state);
    /// <summary>
    /// Remove a move from the schedule
    /// </summary>
    /// <param name="moveId">The move id to remove</param>
    void Remove(int moveId);

    /// <summary>
    /// Update the state of a certain move in the schedule
    /// </summary>
    /// <param name="moveId"></param>
    /// <param name="newState"></param>
    void UpdateState(int moveId, CraneScheduleActivityState newState);

    /// <summary>
    /// Update the crane of a certain move in the schedule
    /// </summary>
    /// <param name="moveId"></param>
    /// <param name="craneId"></param>
    void UpdateCrane(int moveId, int craneId);

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
