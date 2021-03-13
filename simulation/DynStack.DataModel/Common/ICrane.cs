namespace DynStack.DataModel {
  /// <summary>
  /// A crane moves blocks around the system. It can reach various locations.
  /// There can be multiple cranes along the same girder, they cannot however overtake each other.
  /// </summary>
  public interface ICrane {
    /// <summary>
    /// The unique identification for the crane
    /// </summary>
    int Id { get; }
    /// <summary>
    /// The carrying capacity in number of blocks
    /// </summary>
    int Capacity { get; }

    /// <summary>
    /// The width of the crane, space requirement within the girder in length units
    /// </summary>
    double Width { get; }
    /// <summary>
    /// The load of the crane is a stack of blocks
    /// </summary>
    IStack Load { get; }
    /// <summary>
    /// The position of the crane (central point) along the girder.
    /// </summary>
    double GirderPosition { get; set; }
    /// <summary>
    /// The position of the hoist in length units above ground level
    /// </summary>
    double HoistLevel { get; set; }

    /// <summary>
    /// A function that indicates if a crane can reach a certain point within the girder.
    /// </summary>
    /// <param name="girderPosition">The position along the girder.</param>
    /// <returns>True if the crane can move to that position, false if not.</returns>
    bool CanReach(double girderPosition);
  }
}
