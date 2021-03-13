namespace DynStack.DataModel {
  /// <summary>
  /// A block is something that needs to be moved by cranes. It arrives in some place and it leaves the system in another place.
  /// </summary>
  /// <remarks>
  /// Typcially, a block has many properties, but the single common property is that of an <see cref="Id"/>.
  /// </remarks>
  public interface IBlock {
    /// <summary>
    /// The unique identifier for a block.
    /// </summary>
    int Id { get; }
  }
}
