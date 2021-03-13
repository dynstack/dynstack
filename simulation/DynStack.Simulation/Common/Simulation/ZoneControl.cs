using System;
using System.Collections.Generic;
using System.Linq;
using SimSharp;

namespace DynStack.Simulation {
  public interface IZoneControl {
    ZoneRequest Request(double lower, double higher);
    Release Release(ZoneRequest request);
    double GetClosestToTarget(ICraneAgent agent, double to);
    void MoveUpdate();
  }

  public class ZoneControl : IZoneControl {

    protected IStackingEnvironment World { get; private set; }

    protected LinkedList<ZoneRequest> RequestQueue { get; private set; }
    protected Queue<Release> ReleaseQueue { get; private set; }
    protected HashSet<ZoneRequest> Users { get; private set; }
    public ZoneControl(IStackingEnvironment world) {
      World = world;
      RequestQueue = new LinkedList<ZoneRequest>();
      ReleaseQueue = new Queue<Release>();
      Users = new HashSet<ZoneRequest>();
    }

    public virtual ZoneRequest Request(double lower, double higher) {
      var request = new ZoneRequest(World, TriggerRelease, DisposeCallback, lower, higher);
      RequestQueue.AddLast(request);
      TriggerRequest();
      return request;
    }

    public virtual double GetClosestToTarget(ICraneAgent agent, double to) {
      var pos = agent.GetGirderPosition();
      foreach (var u in Users) {
        if (IsOverlap(u.LowerPosition, u.HigherPosition, Math.Min(pos, to), Math.Max(pos, to))) {
          if (u.LowerPosition < pos && pos < u.HigherPosition) {
            World.Environment.Log($"WARNING: Crane {agent.Id} at position {pos} is within blocked zone [{u.LowerPosition}; {u.HigherPosition}] -> should not be!");
            return pos;
          }
          to = to > pos ? u.LowerPosition : u.HigherPosition;
        }
      }
      return to;
    }

    public void MoveUpdate() => TriggerRequest();

    public virtual Release Release(ZoneRequest request) {
      var release = new Release(World.Environment, request, TriggerRequest);
      ReleaseQueue.Enqueue(release);
      TriggerRelease();
      return release;
    }

    protected virtual void DisposeCallback(Event @event) {
      var request = @event as ZoneRequest;
      if (request != null) {
        Release(request);
      }
    }

    protected virtual void DoRequest(ZoneRequest request) {
      if (!Users.Any(x => IsOverlap(x, request))) {
        // there is no other ZoC request overlapping
        var cranesInZoC = World.CraneAgents.Where(x =>
            IsOverlap(Math.Min(x.GetGirderPosition(), x.TargetPosition), Math.Max(x.GetGirderPosition(), x.TargetPosition),
                    request.LowerPosition, request.HigherPosition)).ToList();
        if (cranesInZoC.Count == 0) {
          // there is no crane within the ZoC
          Users.Add(request);
          request.Succeed();
        } else {
          if (cranesInZoC.All(x => x.State == CraneAgentState.Waiting)) {
            // all cranes within the requested ZoC are waiting -> dodge them if possible
            // to calculate this, we compute how much space is available left of the ZoC and how much is available right of the ZoC
            var zocLeft = Users.Where(x => x.HigherPosition <= request.LowerPosition).MaxItems(x => x.HigherPosition).FirstOrDefault();
            var zocRight = Users.Where(x => x.LowerPosition >= request.HigherPosition).MaxItems(x => -x.LowerPosition).FirstOrDefault();
            var cranesLeft = World.CraneAgents.Where(x => x.GetGirderPosition() > (zocLeft?.LowerPosition ?? 0) && x.GetGirderPosition() <= request.LowerPosition).Sum(x => x.Width);
            var cranesRight = World.CraneAgents.Where(x => x.GetGirderPosition() < (zocRight?.HigherPosition ?? World.Width) && x.GetGirderPosition() >= request.HigherPosition).Sum(x => x.Width);
            var spaceLeftOfZoC = request.LowerPosition - (zocLeft?.HigherPosition ?? 0) - cranesLeft;
            var spaceRightOfZoC = (zocRight?.LowerPosition ?? World.Width) - request.HigherPosition - cranesRight;
            if (IsEnoughSpaceForDodge(cranesInZoC, spaceLeftOfZoC, spaceRightOfZoC)) {
              var sumWidth = cranesInZoC.Sum(x => x.Width);
              ICraneAgent goLeft = null, goRight = null;
              foreach (var c in cranesInZoC.OrderBy(x => x.GetGirderPosition())) {
                if (sumWidth <= spaceRightOfZoC) {
                  // we can either go left or right
                  if (c.GetGirderPosition() - request.LowerPosition > request.HigherPosition - c.GetGirderPosition()) {
                    // going right is shorter from this crane onward
                    goRight = c;
                    break;
                  }
                } // not enough space to accomodate all remaining cranes on the right -> this has to go left
                sumWidth -= c.Width;
                goLeft = c;
              }
              // the goLeft and goRight crane will dodge all others to move out of the way
              goLeft?.Dodge(request.LowerPosition - goLeft.Width / 2, 0);
              goRight?.Dodge(request.HigherPosition + goRight.Width / 2, 0);
            }
          }
        }
      }
    }

    private bool IsEnoughSpaceForDodge(List<ICraneAgent> cranes, double spaceLeftOfZoC, double spaceRightOfZoC) {
      // check if we can move cranes to the left respectively right of the ZoC and have enough space to accomodate them
      var left = true;
      foreach (var c in cranes.OrderBy(x => x.GetGirderPosition())) {
        if (left && spaceLeftOfZoC >= c.Width) {
          spaceLeftOfZoC -= c.Width;
          continue;
        }
        // once we have to send cranes to the right, we can't send them left anymore
        // this is an issue only with cranes of different width
        left = false;
        if (spaceRightOfZoC >= c.Width)
          spaceRightOfZoC -= c.Width;
        else return false;
      }
      return true;
    }

    private bool IsOverlap(ZoneRequest user, ZoneRequest newReq) {
      return IsOverlap(user.LowerPosition, user.HigherPosition, newReq.LowerPosition, newReq.HigherPosition);
    }

    private bool IsOverlap(double userLower, double userHigher, double newReqLower, double newReqHigher) {
      return userLower <  newReqLower  && userHigher >= newReqLower // overlap left
          || userLower >= newReqLower  && userHigher <= newReqHigher // contained
          || userLower <  newReqLower  && userHigher >  newReqHigher // contains
          || userLower <= newReqHigher && userHigher >  newReqHigher; // overlap right
    }

    protected virtual void DoRelease(Release release) {
      if (!Users.Remove((ZoneRequest)release.Request))
        throw new InvalidOperationException("Released request does not have a user.");
      release.Succeed();
    }

    protected virtual void TriggerRequest(Event @event = null) {
      while (RequestQueue.Count > 0) {
        var request = RequestQueue.First.Value;
        DoRequest(request);
        if (request.IsTriggered) {
          RequestQueue.RemoveFirst();
        } else break;
      }
    }

    protected virtual void TriggerRelease(Event @event = null) {
      while (ReleaseQueue.Count > 0) {
        var release = ReleaseQueue.Peek();
        if (release.Request.IsAlive) {
          if (!RequestQueue.Remove((ZoneRequest)release.Request))
            throw new InvalidOperationException("Failed to cancel a request.");
          release.Succeed();
          ReleaseQueue.Dequeue();
        } else {
          DoRelease(release);
          if (release.IsTriggered) {
            ReleaseQueue.Dequeue();
          } else break;
        }
      }
    }
  }
}
