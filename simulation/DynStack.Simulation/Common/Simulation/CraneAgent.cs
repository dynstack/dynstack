using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {

  public interface ICraneAgent {
    int Id { get; }
    int Capacity { get; }

    double Width { get; }
    double HoistLevel { get; }
    CraneAgentState State { get; }
    CraneAgentMode Mode { get; }
    CraneAgentDirection Direction { get; }

    double? Priority { get; }

    double TargetPosition { get; }
    double GoalPosition1 { get; }
    double GoalPosition2 { get; }
    double GetGirderPosition();
    double GetGirderDistance();
    double GetHoistDistance();
    bool CanReach(double girderPosition);

    void Cancel();
    void Resume();
    void Dodge(double dodgePosition, double? othersPrio);

    Event WhenPickupOrDropoff();
  }

  public enum CraneAgentDirection { Standing, ToLower, ToUpper }
  public enum CraneAgentState { Waiting, Moving, Picking, Dropping }
  public enum CraneAgentMode { Idle, Dodge, Work }

  public class CraneAgent : ICraneAgent {
    private readonly Process _mainProcess;

    protected IStackingEnvironment _world;
    protected ICrane _crane;
    protected bool _interruptible;
    protected CraneAgentMode _pendingMode;
    protected double? _pendingPrio;
    protected double _dodgePosition;
    protected double _targetGirderPosition;
    protected double _girderSpeed;
    protected double _hoistSpeed;
    protected double _girderDistance;
    protected double _hoistDistance;
    protected TimeStamp _lastUpdate;

    public int Id => _crane.Id;
    public int Capacity => _crane.Capacity;
    public double Width => _crane.Width;
    public double HoistLevel => _crane.HoistLevel;
    public IDistribution<double> GirderSpeed { get; private set; }
    public IDistribution<double> HoistSpeed { get; private set; }
    public IDistribution<double> ManipulationTime { get; private set; }
    public CraneAgentState State { get; internal set; }
    public CraneAgentMode Mode { get; internal set; }
    public CraneAgentDirection Direction {
      get {
        UpdatePosition();
        if (_girderSpeed == 0)
          return CraneAgentDirection.Standing;
        return _girderSpeed < 0 ? CraneAgentDirection.ToLower : CraneAgentDirection.ToUpper;
      }
    }

    public double TargetPosition => _targetGirderPosition;
    public double GoalPosition1 { get; protected set; }
    public double GoalPosition2 { get; protected set; }
    public double? Priority { get; protected set; }

    public double GetGirderPosition() {
      UpdatePosition();
      return _crane.GirderPosition;
    }
    public double GetGirderDistance() {
      UpdatePosition();
      return _girderDistance;
    }
    public double GetHoistDistance() {
      UpdatePosition();
      return _hoistDistance;
    }
    public double GetHoistLevel() {
      UpdatePosition();
      return _crane.HoistLevel;
    }
    public bool CanReach(double girderPosition) => _crane.CanReach(girderPosition);

    public TimeSeriesMonitor Utilization { get; set; }
    protected List<Event> WhenPickupOrDropoffQueue { get; private set; }

    public CraneAgent(IStackingEnvironment world, ICrane crane, IDistribution<double> girderSpeed, IDistribution<double> hoistSpeed, IDistribution<double> manipulationTime) {
      _world = world;
      _crane = crane;
      GirderSpeed = girderSpeed;
      HoistSpeed = hoistSpeed;
      ManipulationTime = manipulationTime;

      _pendingMode = Mode = CraneAgentMode.Work;
      State = CraneAgentState.Waiting;
      GoalPosition1 = GoalPosition2 = _crane.GirderPosition;
      _targetGirderPosition = _crane.GirderPosition;

      _lastUpdate = world.Now;
      _mainProcess = world.Environment.Process(Main());

      WhenPickupOrDropoffQueue = new List<Event>();
    }

    public Event WhenPickupOrDropoff() {
      var evt = new Event(_world.Environment);
      WhenPickupOrDropoffQueue.Add(evt);
      return evt;
    }

    public void Cancel() {
      if (_pendingMode == CraneAgentMode.Idle) return;
      _pendingMode = CraneAgentMode.Idle;
      _pendingPrio = null;
      if (_interruptible) {
        _mainProcess.Interrupt(); // Cancel will even interrupt a current movement if possible
      } else {
        _world.Environment.Log($"INFO: Crane {Id} to cancel, but cannot be interrupted.");
      }
    }

    public void Resume() {
      if (_pendingMode == CraneAgentMode.Work) return;
      _pendingMode = CraneAgentMode.Work;
      _pendingPrio = null;
      if (Mode == CraneAgentMode.Idle && _interruptible) {
        _mainProcess.Interrupt();
      } else {
        _world.Environment.Log($"INFO: Crane {Id} to resume, but not currently idle.");
      }
    }

    public void Dodge(double dodgePosition, double? othersPrio) {
      if (_pendingMode == CraneAgentMode.Dodge) return;
      _pendingMode = CraneAgentMode.Dodge;
      _pendingPrio = (othersPrio + Math.Ceiling((othersPrio ?? 0) + 1e-7)) / 2.0; // propagate priority
      _dodgePosition = dodgePosition;
      if (State == CraneAgentState.Waiting && _interruptible) {
        _mainProcess.Interrupt(); // Dodge will only interrupt main when it's not doing something else
      } else {
        _world.Environment.Log($"INFO: Crane {Id} to dodge, but not currently waiting.");
      }
    }

    protected virtual IEnumerable<Event> Main() {
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
            var moveProcess = _world.Environment.Process(MoveCrane(_dodgePosition, _dodgePosition));
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

    protected virtual IEnumerable<Event> Working() {
      State = CraneAgentState.Waiting;
      Utilization?.UpdateTo(0);
      Priority = null;
      var next = _world.CraneScheduleStore.Get(this, _world);
      _interruptible = true;
      yield return next;
      if (_world.Environment.ActiveProcess.HandleFault()) {
        _world.CraneScheduleStore.Cancel(next);
        yield break;
      }

      if (_pendingMode != CraneAgentMode.Work) yield break;

      var move = next.Move;
      if (move.Amount < 0) {
        _world.Environment.Log($"Crane {Id}: Moving {move.Amount} number of blocks is not possible -> ignore.");
        yield break;
      }
      if (move.Type == MoveType.PickupAndDropoff && move.Amount > _crane.Capacity) {
        _world.Environment.Log($"Cannot {move.Type} {move.Amount} block(s). Crane carries {_crane.Load.Size} of {_crane.Capacity} blocks.");
        yield break;
      }
      if (move.Type == MoveType.PickupAndDropoff && move.Amount == 0 && _crane.Load.Size == 0) {
        _world.Environment.Log($"Crane {Id} should dropoff its current load at {move.PickupLocation}, but does not carry any blocks -> ignore.");
        yield break;
      }

      _world.Environment.Log($"Crane {Id} got move {move.Id} (move blocks [{string.Join(", ", move.MovedBlocks)}] from {move.PickupLocation} to {move.DropoffLocation}).");

      if (move.ReleaseTime > _world.Now) {
        // TODO: Throw, ignore, log, wait, ... !?
        yield return _world.Environment.Timeout(move.ReleaseTime - _world.Now);
        if (_world.Environment.ActiveProcess.HandleFault()) {
          yield break;
        }
      }

      Priority = next.Priority;
      State = CraneAgentState.Moving;
      Utilization?.UpdateTo(1);

      GoalPosition1 = move.PickupGirderPosition;
      if (move.Amount > 0) GoalPosition2 = move.DropoffGirderPosition;
      else GoalPosition2 = GoalPosition1;

      Process doMove;
      yield return (doMove = _world.Environment.Process(MoveCrane(move.PickupGirderPosition, move.DropoffGirderPosition)));
      GoalPosition1 = GoalPosition2;

      if (_world.Environment.ActiveProcess.HandleFault()) {
        doMove.Interrupt();
        _world.Environment.Log($"Crane {Id} interrupted.");
        move.Started.Fail();
        move.Finished.Fail();
        yield break;
      }

      if (move.Type == MoveType.MoveToPickup) {
        move.Started.Succeed();
        move.Finished.Succeed();
        yield break;
      }

      var pickupLoc = _world.LocationResources.Single(x => x.Id == move.PickupLocation);
      if (pickupLoc.Height + _crane.Load.Size < move.Amount) { // any load will first be dropped when picking up blocks
        _world.Environment.Log($"Cannot pickup {move.Amount} blocks from {move.PickupLocation}, only {pickupLoc.Height} blocks there.");
        move.Started.Fail();
        move.Finished.Fail();
        yield break;
      }
      State = CraneAgentState.Picking;
      _interruptible = false;
      move.Started.Succeed();
      yield return _world.Environment.Process(MoveHoist(pickupLoc.Height));
      yield return _world.Environment.Process(DoPickupDropoff(pickupLoc, move.Amount));
      yield return _world.Environment.Process(MoveHoist(_world.Height - _crane.Capacity));

      if (move.Amount > 0) {
        State = CraneAgentState.Moving;
        yield return doMove = _world.Environment.Process(MoveCrane(move.DropoffGirderPosition, move.DropoffGirderPosition));

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
      }

      GoalPosition1 = GoalPosition2 = _crane.GirderPosition;
      move.Finished.Succeed();
      _world.Environment.Log($"Crane {Id} finished move {move.Id} (move blocks [{string.Join(", ", move.MovedBlocks)}] from {move.PickupLocation} to {move.DropoffLocation}).");

      if (move.RaiseHoistAfterService) {
        yield return _world.Environment.Process(MoveHoist(_world.Height - _crane.Capacity));
      }

      _interruptible = true;
    }

    protected IEnumerable<Event> DoPickupDropoff(ILocationResource loc, int pickupSize) {
      if (_crane.Load.Size > 0) {
        // first drop-off everything
        var time = ManipulationTime.GetValue();
        yield return _world.Environment.Timeout(TimeSpan.FromSeconds(time));
        yield return loc.Dropoff(_crane.Load);
        _crane.Load.Clear();
      }
      if (pickupSize > 0) {
        // move hoist to new position
        if (loc.Height < pickupSize) {
          _world.Environment.Log($"Warning: Attempting to pickup more blocks ({pickupSize}) than there are ({loc.Height}).");
        }
        yield return _world.Environment.Process(MoveHoist(loc.Height - pickupSize));
        var pickupEvent = loc.Pickup(pickupSize);
        pickupEvent.AddCallback(e => _crane.Load.AddToBottom(pickupEvent.Stack));
        // pickup
        var time = ManipulationTime.GetValue();
        yield return _world.Environment.Timeout(TimeSpan.FromSeconds(time)) & pickupEvent;
        if (pickupEvent.IsAlive) throw new InvalidOperationException($"Cannot pickup from stack {loc.Id}");
      }
      TriggerWhenPickupOrDropoff();
    }

    protected virtual IEnumerable<Event> MoveCrane(double targetPosition, double goalPos2) {
      UpdatePosition();
      GoalPosition1 = targetPosition;
      GoalPosition2 = goalPos2;

      var speed = GirderSpeed.GetValue();

      while (Math.Abs(targetPosition - _crane.GirderPosition) > 0.01) {
        var collider = targetPosition != _crane.GirderPosition ? GetPotentialCollider() : null;
        _targetGirderPosition = _crane.GirderPosition;

        if (collider == null) {
          _targetGirderPosition = _world.ZoneControl.GetClosestToTarget(this, targetPosition);

          // CASE 1: No collision expected, move to target
          var sftDist = _crane.Width / 2;
          if (_crane.GirderPosition < _targetGirderPosition) sftDist = -sftDist;
          var stackHeight = _world.HeightBetween(_crane.GirderPosition + sftDist, targetPosition - sftDist);

          Process hoist = null;
          if (_crane.HoistLevel < stackHeight + 1)
            yield return hoist = _world.Environment.Process(MoveHoist(stackHeight + 1));

          if (hoist != null && _world.Environment.ActiveProcess.HandleFault()) {
            hoist.Interrupt();
            break;
          }

          _girderSpeed = _targetGirderPosition < _crane.GirderPosition ? -speed : speed;
          var timeout = _world.Environment.Timeout(TimeSpan.FromSeconds(Math.Abs(_targetGirderPosition - _crane.GirderPosition) / Math.Abs(_girderSpeed)));
          timeout.AddCallback(_ => { UpdatePosition(); _girderSpeed = 0; _crane.GirderPosition = _targetGirderPosition; });
          yield return timeout & _world.ReactionTime();
          _world.ZoneControl.MoveUpdate();
        } else {
          // CASE 2: Avoid potential collision
          if (collider.State == CraneAgentState.Waiting) {
            // CASE 2a: collider is idleing, tell it to dodge
            var dodgePoint = targetPosition < _crane.GirderPosition ? Math.Min(targetPosition, GoalPosition2) - Width / 2 - collider.Width / 2
                                                                   : Math.Max(targetPosition, GoalPosition2) + Width / 2 + collider.Width / 2;
            if (collider.Mode == CraneAgentMode.Idle) {
              Utilization?.UpdateTo(0);
              yield return _world.ReactionTime();
              Utilization?.UpdateTo(1);
            } else {
              collider.Dodge(dodgePoint, Priority);
              _targetGirderPosition = _world.ZoneControl.GetClosestToTarget(this, targetPosition < _crane.GirderPosition
                ? collider.GetGirderPosition() + collider.Width / 2 + Width / 2
                : collider.GetGirderPosition() - collider.Width / 2 - Width / 2);
              if (_crane.GirderPosition < targetPosition && _targetGirderPosition > targetPosition
                || _crane.GirderPosition > targetPosition && _targetGirderPosition < targetPosition)
                _targetGirderPosition = targetPosition; // don't move past our target
              var sftDist = _crane.Width / 2;
              if (_crane.GirderPosition < _targetGirderPosition) sftDist = -sftDist;
              var stackHeight = _world.HeightBetween(_crane.GirderPosition + sftDist, targetPosition - sftDist);

              Process hoist = null;
              if (_crane.HoistLevel < stackHeight + 1)
                yield return hoist = _world.Environment.Process(MoveHoist(stackHeight + 1));

              if (hoist != null && _world.Environment.ActiveProcess.HandleFault()) {
                hoist.Interrupt();
                break;
              }

              _girderSpeed = _targetGirderPosition < _crane.GirderPosition ? -speed : speed;
              var timeout = _world.Environment.Timeout(TimeSpan.FromSeconds(Math.Abs(_targetGirderPosition - _crane.GirderPosition) / Math.Abs(_girderSpeed)));
              timeout.AddCallback(_ => { UpdatePosition(); _girderSpeed = 0; _crane.GirderPosition = _targetGirderPosition; });
              yield return timeout & _world.ReactionTime();
              _world.ZoneControl.MoveUpdate();
            }
          } else {
            // Case 2b: The collider is moving or servicing
            // Calculate the point at which a collision would occur, either the other's current position or its target position
            var collisionPoint = targetPosition < _crane.GirderPosition ? Math.Max(collider.GetGirderPosition(), collider.TargetPosition) + collider.Width / 2 + Width / 2
                                                                        : Math.Min(collider.GetGirderPosition(), collider.TargetPosition) - collider.Width / 2 - Width / 2;

            var dodgePoint = targetPosition < _crane.GirderPosition ? Math.Max(Math.Max(Math.Max(collider.GetGirderPosition(), collider.GoalPosition1), collider.GoalPosition2), collider.TargetPosition) + collider.Width / 2 + Width / 2
                                                                    : Math.Min(Math.Min(Math.Min(collider.GetGirderPosition(), collider.GoalPosition1), collider.GoalPosition2), collider.TargetPosition) - collider.Width / 2 - Width / 2;
            if (Priority > collider.Priority) {
              var tgt = _crane.GirderPosition < collider.GetGirderPosition() ? Math.Min(dodgePoint, targetPosition) - 1e-7 : Math.Max(dodgePoint, targetPosition) + 1e-7;
              if (Math.Abs(tgt - _crane.GirderPosition) > 1e-5) {
                var oldPrio = Priority;
                // to propagate priority onto dodgers e.g. 4 -> 4.5 -> 4.75 -> 4.875
                // this is necessary so that a dodger is able to cause further cranes to dodge
                Priority = ((collider.Priority ?? 0) + Math.Floor((collider.Priority ?? 0) + 1)) / 2.0;
                if (Math.Abs(_crane.GirderPosition - tgt) < 1e-5) {
                  Utilization?.UpdateTo(0);
                  yield return _world.ReactionTime();
                  Utilization?.UpdateTo(1);
                } else {
                  yield return _world.AtLeastReactionTime(MoveCrane(tgt, tgt));
                }
                GoalPosition1 = targetPosition;
                GoalPosition2 = goalPos2;
                Priority = oldPrio;
              } else {
                Utilization?.UpdateTo(0);
                yield return _world.ReactionTime();
                Utilization?.UpdateTo(1);
              }
            } else {
              _targetGirderPosition = _world.ZoneControl.GetClosestToTarget(this, _crane.GirderPosition < collider.GetGirderPosition()
                ? Math.Min(targetPosition, collisionPoint)
                : Math.Max(targetPosition, collisionPoint));

              var sftDist = _crane.Width / 2;
              if (_crane.GirderPosition < _targetGirderPosition) sftDist = -sftDist;
              var stackHeight = _world.HeightBetween(_crane.GirderPosition + sftDist, targetPosition - sftDist);

              Process hoist = null;
              if (_crane.HoistLevel < stackHeight + 1)
                yield return hoist = _world.Environment.Process(MoveHoist(stackHeight + 1));

              if (hoist != null && _world.Environment.ActiveProcess.HandleFault()) {
                hoist.Interrupt();
                break;
              }

              _girderSpeed = _targetGirderPosition < _crane.GirderPosition ? -speed : speed;
              var timeout = _world.Environment.Timeout(TimeSpan.FromSeconds(Math.Abs(_targetGirderPosition - _crane.GirderPosition) / Math.Abs(_girderSpeed)));
              timeout.AddCallback(_ => { UpdatePosition(); _girderSpeed = 0; _crane.GirderPosition = _targetGirderPosition; });
              yield return timeout & _world.ReactionTime();
              _world.ZoneControl.MoveUpdate();
            }
          }
        }

        UpdatePosition();

        if (_world.Environment.ActiveProcess.HandleFault()) {
          break;
        }
      }

      StopCrane();
    }

    private ICraneAgent GetPotentialCollider() {
      var collider = _world.CraneAgents.Where(x => x.Id != Id && PotentialCausingCollision(x))
        .MaxItems(x => -Math.Abs(x.GetGirderPosition() - _crane.GirderPosition)).SingleOrDefault();
      return collider;
    }

    private bool PotentialCausingCollision(ICraneAgent other) {
      if (GoalPosition1 < _crane.GirderPosition && other.GetGirderPosition() < GetGirderPosition()) {
        // crane may cause collision with other in two cases
        if (other.GoalPosition1 <= other.GetGirderPosition()) {
          // 0 .... <-other .... <-crane .... MAX
          return other.GetGirderPosition() + other.Width / 2 > Math.Min(Math.Min(GoalPosition1, GoalPosition2), TargetPosition) - Width / 2;
        } else {
          // 0 .... other-> .... <-crane .... MAX
          return Math.Max(Math.Max(other.GoalPosition1, other.GoalPosition2), other.TargetPosition) + other.Width / 2 > Math.Min(Math.Min(GoalPosition1, GoalPosition2), TargetPosition) - Width / 2;
        }
      } else if (GoalPosition1 > _crane.GirderPosition && other.GetGirderPosition() > GetGirderPosition()) {
        // crane may cause collision with other in two cases
        if (other.GoalPosition1 >= other.GetGirderPosition()) {
          // 0 .... crane-> .... other-> .... MAX
          return other.GetGirderPosition() - other.Width / 2 < Math.Max(Math.Max(GoalPosition1, GoalPosition2), TargetPosition) + Width / 2;
        } else {
          // 0 .... crane-> .... <-other .... MAX
          return Math.Min(Math.Min(other.GoalPosition1, other.GoalPosition2), other.TargetPosition) - other.Width / 2 < Math.Max(Math.Max(GoalPosition1, GoalPosition2), TargetPosition) + Width / 2;
        }
      }
      return false;
    }

    protected IEnumerable<Event> MoveHoist(double targetLevel) {
      UpdatePosition();
      _hoistSpeed = targetLevel < _crane.HoistLevel ? -HoistSpeed.GetValue() : HoistSpeed.GetValue();
      var duration = TimeSpan.FromSeconds(Math.Abs(targetLevel - _crane.HoistLevel) / Math.Abs(_hoistSpeed));
      if (duration > TimeSpan.Zero)
        yield return _world.Environment.Timeout(duration);
      UpdatePosition();
      _hoistSpeed = 0;
      _crane.HoistLevel = targetLevel;
      _world.Environment.ActiveProcess.HandleFault();
    }

    protected void StopCrane() {
      UpdatePosition();
      _targetGirderPosition = _crane.GirderPosition;
      _girderSpeed = 0;
    }

    protected void UpdatePosition() {
      var now = _world.Now;
      if (_lastUpdate == now) return;
      var duration = now - _lastUpdate;
      var girderDelta = _girderSpeed * duration.TotalSeconds;
      _crane.GirderPosition += girderDelta;
      var hoistDelta = _hoistSpeed * duration.TotalSeconds;
      _crane.HoistLevel += hoistDelta;
      _girderDistance += Math.Abs(girderDelta);
      _hoistDistance += Math.Abs(hoistDelta);
      _lastUpdate = now;
    }

    private void TriggerWhenPickupOrDropoff() {
      if (WhenPickupOrDropoffQueue.Count == 0) return;
      foreach (var evt in WhenPickupOrDropoffQueue)
        evt.Succeed();
      WhenPickupOrDropoffQueue.Clear();
    }
  }
}
