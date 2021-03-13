namespace DynStack.DataModel {
  /// <summary>
  /// A location is a place that contains blocks and which can be reached by a crane.
  /// </summary>
  public interface ILocation {
    /// <summary>
    /// The unique identifier of the location.
    /// </summary>
    int Id { get; }
    /// <summary>
    /// The maximum number of blocks that can be stacked at this location.
    /// </summary>
    int MaxHeight { get; }
    /// <summary>
    /// The number of blocks that can still be stacked given the current stack of blocks.
    /// </summary>
    int FreeHeight { get; }
    /// <summary>
    /// The current number of blocks that are stacked at this location.
    /// </summary>
    int Height { get; }
    /// <summary>
    /// The actual stack of blocks
    /// </summary>
    IStack Stack { get; }
    /// <summary>
    /// The topmost block in the stack
    /// </summary>
    IBlock Topmost { get; }
    /// <summary>
    /// The position of the location within the 1-dimensional coordinate system of the girder
    /// </summary>
    double GirderPosition { get; }

    /// <summary>
    /// A manipulation operation that removes the topmost block from the location and returns it.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when the location does not contain any blocks.</exception>
    /// <returns>The topmost block is removed and returned.</returns>
    IBlock Pickup();
    /// <summary>
    /// A manipulation operation that removes the <paramref name="amt"/>-topmost blocks from the location
    /// and returns it as a stack of blocks.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when there are not enough blocks.</exception>
    /// <param name="amt">The number of blocks to remove.</param>
    /// <returns>The topmost blocks as a stack of blocks.</returns>
    IStack Pickup(int amt);
    /// <summary>
    /// A manipulation operation that stores an additional block at the top of the stack.
    /// </summary>
    /// <exception cref="System.InvalidOperationException">Thrown when the <see cref="FreeHeight"/> of the location is 0.</exception>
    /// <param name="b">The block that is stacked on top.</param>
    void Dropoff(IBlock b);
    /// <summary>
    /// A manipulation operation that stores an additional number of blocks given as a stack of blocks.
    /// </summary>
    /// <remarks>The <paramref name="stack"/> itself is not manipulated.</remarks>
    /// <exception cref="System.InvalidOperationException">Thrown when the <see cref="FreeHeight"/> of the location is less than the size of the <paramref name="stack"/>.</exception>
    /// <param name="stack">The stack of blocks to be dropped at the top of the current stack of blocks.</param>
    void Dropoff(IStack stack);
  }
}
