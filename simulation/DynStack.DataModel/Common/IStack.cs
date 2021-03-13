using System.Collections.Generic;
using System.Linq;

namespace DynStack.DataModel {
  /// <summary>
  /// A stack of blocks is an organization of blocks whereby each block is directly above or below exactly one other block.
  /// It may contain 0 blocks or more. There is no limit in the amount of blocks that a stack is comprised of.
  /// </summary>
  public interface IStack {
    /// <summary>
    /// The amount of blocks in the stack
    /// </summary>
    int Size { get; }
    /// <summary>
    /// The actual enumeration of blocks in order from botttom (first element) to top (last element).
    /// </summary>
    IEnumerable<IBlock> BottomToTop { get; }
    /// <summary>
    /// The actual enumeration of blocks in order from top (first element) to bottom (last element).
    /// </summary>
    IEnumerable<IBlock> TopToBottom { get; }
    /// <summary>
    /// A manipulation operation that clears the stack and removes all blocks.
    /// </summary>
    void Clear();
    /// <summary>
    /// A manipulation operation that puts one block on top of the stack.
    /// </summary>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="block"/> is null.</exception>
    /// <exception cref="System.ArgumentException">Thrown when the type of <paramref name="block"/> is not of the expected type.
    /// Usually, there can only be one type of block.
    /// </exception>
    /// <param name="block">The block that is now on top (last element).</param>
    void AddOnTop(IBlock block);
    /// <summary>
    /// A manipulation operation that puts another stack of blocks on top of the stack.
    /// </summary>
    /// <remarks>
    /// The <paramref name="stack"/> is not manipulated.
    /// </remarks>
    /// <exception cref="System.InvalidCastException">Thrown when the type of the blocks in the <paramref name="stack"/> are not of the expected type.</exception>
    /// <param name="stack">The stack of blocks that should be put on top.</param>
    void AddOnTop(IStack stack);
    /// <summary>
    /// A manipulation operation that puts one block at the bottom of the stack.
    /// For instance, a crane that picks up a block may add it to its bottom.
    /// </summary>
    /// <exception cref="System.ArgumentNullException">Thrown when <paramref name="block"/> is null.</exception>
    /// <exception cref="System.ArgumentException">Thrown when the type of <paramref name="block"/> is not of the expected type.
    /// Usually, there can only be one type of block.</exception>
    /// <param name="block">The block that is now at the bottom (first element).</param>
    void AddToBottom(IBlock block);
    /// <summary>
    /// A manipulation operation that puts another stack of blocks at the bottom of the stack.
    /// </summary>
    /// <remarks>
    /// The <paramref name="stack"/> is not manipulated.
    /// </remarks>
    /// <exception cref="System.InvalidCastException">Thrown when the type of the blocks in the <paramref name="stack"/> are not of the expected type.</exception>
    /// <param name="stack">The stack of blocks that should be inserted into the bottom.</param>
    void AddToBottom(IStack stack);

    /// <summary>
    /// A manipulation operation that removes the topmost block and returns it.
    /// </summary>
    /// <exception cref="System.IndexOutOfRangeException">Thrown when there are not enough blocks.</exception>
    /// <returns>The block that is (was) topmost.</returns>
    IBlock RemoveFromTop();
    /// <summary>
    /// A manipulation operation that removes the <paramref name="amount"/>-topmost blocks and returns it.
    /// </summary>
    /// <exception cref="System.IndexOutOfRangeException">Thrown when there are not enough blocks.</exception>
    /// <param name="amount">The number of blocks to remove.</param>
    /// <returns>The topmost blocks as stack.</returns>
    IStack RemoveFromTop(int amount);
    /// <summary>
    /// A manipulation operation that removes the bottom-most block and returns it.
    /// </summary>
    /// <exception cref="System.IndexOutOfRangeException">Thrown when there are not enough blocks.</exception>
    /// <returns>The block that is (was) at the bottom.</returns>
    IBlock RemoveFromBottom();
    /// <summary>
    /// A manipulation operation that removes the <paramref name="amount"/>-topmost blocks and returns it.
    /// </summary>
    /// <exception cref="System.IndexOutOfRangeException">Thrown when there are not enough blocks.</exception>
    /// <param name="amount">The number of blocks to remove.</param>
    /// <returns>The bottom-most blocks as stack.</returns>
    IStack RemoveFromBottom(int amount);
  }
}
