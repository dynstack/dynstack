namespace DynStack.DataModel {
  /// <summary>
  /// A move request is generated when a certain block should be put in a specified position at 
  /// a target stack.
  /// </summary>
  public interface IMoveRequest {
    /// <summary>
    /// The identifier for this request.
    /// </summary>
    int Id { get; }
    /// <summary>
    /// The target location where the <see cref="BlockId"/> should be moved to.
    /// </summary>
    int TargetLocationId { get; }
    /// <summary>
    /// The block that is requested to be moved to the <see cref="TargetLocationId"/>.
    /// </summary>
    int BlockId { get; }
    /// <summary>
    /// The time before which the request should be completed.
    /// </summary>
    TimeStamp DueDate { get; }
  }
}
