using System;
using System.Collections.Generic;
using DynStack.DataModel;

namespace DynStack.Simulation {
  public interface IStackingEnvironment {
    SimSharp.Simulation Environment { get; } // TODO: Environment name is not so good
    TimeStamp Now { get; }
    int Height { get; }
    double Width { get; }
    IEnumerable<ILocationResource> LocationResources { get; }
    IEnumerable<IBlock> Blocks { get; }
    IEnumerable<ICraneAgent> CraneAgents { get; }
    IEnumerable<ICraneMoveEvent> CraneMoves { get; }
    IEnumerable<IMoveRequest> MoveRequests { get; }
    ICraneScheduleStore CraneScheduleStore { get; }
    IZoneControl ZoneControl { get; }

    SimSharp.Timeout ReactionTime();
    SimSharp.Timeout AtLeastReactionTime(TimeSpan actual);
    SimSharp.Event AtLeastReactionTime(IEnumerable<SimSharp.Event> generator, int priority = 0);

    int HeightBetween(double girderPosition, double targetPosition);
    void MoveFinished(int moveId, int craneId, TimeStamp started, (int, int, int) hoistDistances, CraneMoveTermination result);
  }
}
