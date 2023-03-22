using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using DynStack.DataModel.CS;
using SimSharp;

namespace DynStack.Simulation.CS {
  public class CSCraneAgent : CraneAgent {
    protected World _csWorld => (World)_world;

    public ICraneMoveEvent CurrentMove { get; private set; }
    private bool _pickupCompleted;

    public CSCraneAgent(
      IStackingEnvironment world,
      ICrane crane,
      IDistribution<double> girderSpeed,
      IDistribution<double> hoistSpeed,
      IDistribution<double> manipulationTime
    ) : base(
      world,
      crane,
      girderSpeed,
      hoistSpeed,
      manipulationTime
    ) {
    }

    protected override IEnumerable<Event> Main() {
      while (true) {
        Priority = _pendingPrio;
        Mode = _pendingMode;
        switch (Mode) {
          case CraneAgentMode.Idle:
            State = CraneAgentState.Waiting;
            Utilization?.UpdateTo(0);
            _interruptible = true;
            yield return new Event(_world.Environment);
            _world.Environment.ActiveProcess.HandleFault();
            continue;
          case CraneAgentMode.Dodge:
            State = CraneAgentState.Moving;
            Utilization?.UpdateTo(1);
            _interruptible = true;
            var moveProcess = _world.Environment.Process(MoveCrane(_dodgePosition));
            yield return moveProcess;
            if (_world.Environment.ActiveProcess.HandleFault()) {
              moveProcess.Interrupt();
              _interruptible = false;
              yield return moveProcess;
              continue;
            }
            _pendingMode = CraneAgentMode.Work;
            _pendingPrio = null;
            break;
          case CraneAgentMode.Work:
            var workingProcess = _world.Environment.Process(Working());
            _interruptible = true;
            yield return workingProcess;
            if (_world.Environment.ActiveProcess.HandleFault()) {
              if (workingProcess.IsAlive) workingProcess.Interrupt();
              _interruptible = false;
              if (workingProcess.IsAlive) yield return workingProcess;
              continue;
            }
            break;
          default: throw new InvalidOperationException($"Error in CraneAgent, unknown mode: {_pendingMode}.");
        }
      }
    }


    protected override IEnumerable<Event> Working() {
      State = CraneAgentState.Waiting;
      Utilization?.UpdateTo(0);
      Priority = null;
      var next = _world.CraneScheduleStore.Get(this, _world);
      _interruptible = true;

      if (next.Move == null) {
        yield return next;

        if (_world.Environment.ActiveProcess.HandleFault()) {
          _world.CraneScheduleStore.Cancel(next);
          yield break;
        }
      }

      if (_pendingMode != CraneAgentMode.Work) yield break;

      var move = CurrentMove = next.Move;

      _world.Environment.Log($"Crane {Id} got move {move.Id} (move blocks [{string.Join(", ", move.MovedBlocks)}] from {move.PickupLocation} to {move.DropoffLocation}).");

      if (move.ReleaseTime > _world.Now) {
        // todo: does not currently work
        throw new NotSupportedException("release times are currenlty not supported");
        // TODO: Throw, ignore, log, wait, ... !?
        yield return _world.Environment.Timeout(move.ReleaseTime - _world.Now);
        if (_world.Environment.ActiveProcess.HandleFault()) {
          yield break;
        }
      }

      // once a crane starts its move, it cannot be interrupted anymore
      // crane will handle evasion by itself
      _interruptible = false;

      Priority = next.Priority;
      State = CraneAgentState.Moving;
      Utilization?.UpdateTo(1);

      GoalPosition1 = move.PickupGirderPosition;
      switch (move.Type) {
        case MoveType.PickupAndDropoff:
          GoalPosition2 = move.DropoffGirderPosition;
          break;
        case MoveType.MoveToPickup:
          GoalPosition2 = move.PickupGirderPosition;
          break;
        default: throw new ArgumentOutOfRangeException();
      }

      move.Started.Succeed();

      // move to pickup position
      // MoveCrane respects other cranes and does evasion
      yield return _world.Environment.Process(MoveCrane(GoalPosition1));

      if (move.Type == MoveType.MoveToPickup) {
        move.Finished.Succeed();
        yield break;
      }

      var pickupLoc = _world.LocationResources.Single(x => x.Id == move.PickupLocation);
      if (pickupLoc.Height + _crane.Load.Size < move.Amount) { // any load will first be dropped when picking up blocks
        _world.Environment.Log($"Cannot pickup {move.Amount} blocks from {move.PickupLocation}, only {pickupLoc.Height} blocks there.");
        move.Finished.Fail();
        yield break;
      }

      State = CraneAgentState.Picking;

      yield return _world.Environment.Process(MoveHoist(pickupLoc.Height));
      yield return _world.Environment.Process(DoPickupDropoff(pickupLoc, move.Amount));
      yield return _world.Environment.Process(MoveHoist(_world.Height - _crane.Capacity));
      _pickupCompleted = true;

      State = CraneAgentState.Moving;

      yield return _world.Environment.Process(MoveCrane(GoalPosition2));

      var dropoffLoc = _world.LocationResources.Single(x => x.Id == move.DropoffLocation);
      if (dropoffLoc.FreeHeight < move.Amount) {
        _world.Environment.Log($"Cannot drop off {move.Amount} blocks at {move.DropoffLocation}, only {dropoffLoc.FreeHeight} free space.");
        move.Finished.Fail();
        _interruptible = true;
        yield break;
      }

      State = CraneAgentState.Dropping;

      yield return _world.Environment.Process(MoveHoist(dropoffLoc.Height));
      yield return _world.Environment.Process(DoPickupDropoff(dropoffLoc, 0));
      yield return _world.Environment.Process(MoveHoist(_world.Height - _crane.Capacity));

      _pickupCompleted = false;
      GoalPosition1 = GoalPosition2 = _crane.GirderPosition;
      CurrentMove = null;
      Priority = null;
      move.Finished.Succeed();

      _world.Environment.Log($"Crane {Id} finished move {move.Id} (move blocks [{string.Join(", ", move.MovedBlocks)}] from {move.PickupLocation} to {move.DropoffLocation}).");

      _interruptible = true;
    }

    protected virtual IEnumerable<Event> MoveCrane(double targetPosition) {
      UpdatePosition();

      var speed = GirderSpeed.GetValue();
      var sftDist = _crane.Width / 2;

      while (Math.Abs(targetPosition - _crane.GirderPosition) > 0.01) {
        var collider = (CSCraneAgent)_world.CraneAgents.First(x => x != this);

        if (collider == null) {
          // CASE 1: No collision expected, move to target
          _targetGirderPosition = targetPosition;
        } else {
          // CASE 2: Avoid potential collision
          if (collider.State == CraneAgentState.Waiting) {
            if (collider.CurrentMove == null) {
              // collider is idling with no move set, tell it to dodge
              var dodgePoint = ComputeDodgePoint(this, collider);
              collider.Dodge(dodgePoint, Priority);
            }
          }

          _targetGirderPosition = ComputeDodgePoint(collider, this);
        }

        if (_crane.GirderPosition < _targetGirderPosition) sftDist = -sftDist;
        var stackHeight = _world.HeightBetween(_crane.GirderPosition + sftDist, _targetGirderPosition - sftDist);

        if (_crane.HoistLevel < stackHeight + 1)
          yield return _world.Environment.Process(MoveHoist(stackHeight + 1));

        _girderSpeed = _targetGirderPosition < _crane.GirderPosition ? -speed : speed;
        var timeout = _world.Environment.Timeout(TimeSpan.FromSeconds(Math.Abs(_targetGirderPosition - _crane.GirderPosition) / Math.Abs(_girderSpeed)));
        timeout.AddCallback(_ => { UpdatePosition(); _girderSpeed = 0; _crane.GirderPosition = _targetGirderPosition; });

        yield return timeout & _world.ReactionTime();
        _world.ZoneControl.MoveUpdate();

        UpdatePosition();

        if (Math.Abs(targetPosition - _crane.GirderPosition) > 0.01) {
          Utilization?.UpdateTo(0);
          yield return _world.ReactionTime();
          Utilization?.UpdateTo(1);
        }
      }

      StopCrane();
    }

    private static double GetMinMax(double a, double b, bool max) {
      return max ? Math.Max(a, b) : Math.Min(a, b);
    }

    private static double ComputeDodgePoint(CSCraneAgent a, CSCraneAgent b) {
      var aPos = a.GetGirderPosition();
      var bPos = b.GetGirderPosition();
      var dodgePoint = aPos;

      var aPrio = a.Priority ?? int.MaxValue;
      var bPrio = b.Priority ?? int.MaxValue;

      var aMove = aPrio <= bPrio ? a.CurrentMove : null;
      var bMove = b.CurrentMove;
      var bGoal = bMove == null
        ? bPos
        : b._pickupCompleted
          ? bMove.DropoffGirderPosition
          : bMove.PickupGirderPosition;

      var max = aPos < bPos;

      if (aMove != null) {
        if (!a._pickupCompleted) {
          dodgePoint = GetMinMax(dodgePoint, aMove.PickupGirderPosition, max);
        }
        dodgePoint = GetMinMax(dodgePoint, aMove.DropoffGirderPosition, max);
      }

      var sd = (a.Width + b.Width) / 2.0;
      dodgePoint = max ? dodgePoint + sd : dodgePoint - sd;

      return GetMinMax(dodgePoint, bGoal, max);
    }
  }
}
