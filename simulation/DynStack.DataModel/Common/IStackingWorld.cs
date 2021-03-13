using System.Collections.Generic;
using ProtoBuf.Meta;

namespace DynStack.DataModel {
  /// <summary>
  /// The world object contains all the required entities: locations, blocks, cranes, moves, and move requests
  /// </summary>
  public interface IStackingWorld {
    /// <summary>
    /// The current time
    /// </summary>
    TimeStamp Now { get; }
    /// <summary>
    /// The height of the world (height of the girder)
    /// </summary>
    int Height { get; }
    /// <summary>
    /// The width of the world (length of the girder)
    /// </summary>
    double Width { get; }
    /// <summary>
    /// The enumeration of all locations that are known
    /// </summary>
    IEnumerable<ILocation> Locations { get; }
    /// <summary>
    /// The enumeration of all blocks that are known
    /// </summary>
    IEnumerable<IBlock> Blocks { get; }
    /// <summary>
    /// The enumeration of all cranes that are known
    /// </summary>
    IEnumerable<ICrane> Cranes { get; }
    /// <summary>
    /// The enumeration of all moves that are to be performed.
    /// </summary>
    IEnumerable<IMove> Moves { get; }
    /// <summary>
    /// The enumeration of all move requests that are to be performed.
    /// </summary>
    IEnumerable<IMoveRequest> MoveRequests { get; }
    /// <summary>
    /// The current crane schedule that assigns moves to cranes and sequences them.
    /// </summary>
    ICraneSchedule CraneSchedule { get; }

    /// <summary>
    /// Returns the data model of the involved entities in protobuf syntax
    /// </summary>
    /// <param name="syntax">The protobuf syntax version</param>
    /// <returns>The string that comprises the contents of a respective .proto file</returns>
    string GetDataModel(ProtoSyntax syntax = ProtoSyntax.Proto3);
  }
}
