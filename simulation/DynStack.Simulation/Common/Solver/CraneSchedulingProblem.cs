using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DynStack.DataModel;

namespace DynStack.Simulation.Common {
  public struct CraneInfo {
    public int CraneId { get; }
    public double PosPrePick { get; set; }
    public double PosAtPick { get; set; }
    public double PosAtDrop { get; set; }
    public double TimeAtPick { get; set; }
    public double TimeAtDrop { get; set; }

    public CraneInfo(int craneId, double initialPos) {
      CraneId = craneId;
      PosPrePick = initialPos;
      PosAtPick = 0;
      PosAtDrop = 0;
      TimeAtPick = 0;
      TimeAtDrop = 0;
    }
    public CraneInfo(CraneInfo last) {
      CraneId = last.CraneId;
      PosPrePick = last.PosAtDrop;
      PosAtPick = 0;
      PosAtDrop = 0;
      TimeAtPick = 0;
      TimeAtDrop = 0;
    }
  }
  public class CraneSchedulingProblem {
    public static double Evaluate(IStackingWorld world, IList<(int moveId, int craneId)> sequence, double speed, double serviceTime) {
      var activities = sequence.Count;
      var moves = world.Moves.ToDictionary(x => x.Id, x => x);
      var cranes = world.Cranes.ToDictionary(x => x.Id, x => x);

      var craneInfos = Enumerable.Range(0, activities).Select(x => new Dictionary<int, CraneInfo>()).ToArray();
      var maxTime = new double[activities];
      var solution = new int[activities - 1, 4];

      var infeasible = false;

      var change = false;
      for (var i = 0; i < sequence.Count; i++) {
        change = false;
        for (var j = 0; j < sequence.Count - 1; j++) {
          for (var k = j + 1; k < sequence.Count; k++) {
            if (moves[sequence[j].moveId].PredecessorIds.Contains(sequence[k].moveId)) {
              var h = sequence[j];
              sequence[j] = sequence[k];
              sequence[k] = h;
              change = true;
            }
          }
        }
        if (!change) break;
      }
      if (change) return 1000000; // could not repair schedule

      // calculate the crane positions that arise out of the schedule
      for (var i = 0; i < activities; i++) {
        var c = sequence[i].craneId;
        // TODO: safety distance in the next call assumes all cranes have same width
        var conflictTime = GetLatestTimeOfConflict(sequence, i, c, craneInfos, maxTime, moves, cranes[c].Width);

        var infoC = i == 0 ? new CraneInfo(c, cranes[c].GirderPosition) : new CraneInfo(craneInfos[i - 1][c]);
        infoC.PosAtPick = moves[sequence[i].moveId].PickupGirderPosition;
        infoC.PosAtDrop = moves[sequence[i].moveId].DropoffGirderPosition;
        var dhC = Math.Abs(infoC.PosAtPick - infoC.PosPrePick) / speed;
        var flowC = Math.Abs(infoC.PosAtDrop - infoC.PosAtPick) / speed;
        infoC.TimeAtPick = dhC + Math.Max(infoC.TimeAtDrop, Math.Max(moves[sequence[i].moveId].ReleaseTime.MilliSeconds, conflictTime));
        infoC.TimeAtDrop = infoC.TimeAtPick + flowC + serviceTime;
        craneInfos[i][c] = infoC;

        var safetyDistance = cranes[c].Width / 2;
        foreach (var left in cranes.Keys.Where(l => l < c).OrderByDescending(l => l)) {
          var infoLeft = i == 0 ? new CraneInfo(left, cranes[left].GirderPosition) : new CraneInfo(craneInfos[i - 1][left]);
          infoLeft.PosAtPick = Math.Min(infoLeft.PosPrePick, infoC.PosAtPick - safetyDistance - cranes[left].Width / 2);
          infoLeft.PosAtDrop = Math.Min(infoLeft.PosAtPick, infoC.PosAtDrop - safetyDistance - cranes[left].Width / 2);
          var dhLeft = Math.Abs(infoLeft.PosAtPick - infoLeft.PosPrePick) / speed;
          var flowLeft = Math.Abs(infoLeft.PosAtDrop - infoLeft.PosAtPick) / speed;
          infoLeft.TimeAtPick = dhLeft + craneInfos[i - 1][left].TimeAtDrop;
          infoLeft.TimeAtDrop = infoLeft.TimeAtPick + flowLeft;

          safetyDistance += cranes[left].Width;
          if (infoLeft.PosAtPick < 0 || infoLeft.PosAtPick > world.Width
            || infoLeft.PosAtDrop < 0 || infoLeft.PosAtDrop > world.Width) infeasible = true;
          craneInfos[i][left] = infoLeft;
        }
        safetyDistance = cranes[c].Width / 2;
        foreach (var right in cranes.Keys.Where(r => r > c).OrderBy(r => r)) {
          var infoRight = i == 0 ? new CraneInfo(right, cranes[right].GirderPosition) : new CraneInfo(craneInfos[i - 1][right]);
          infoRight.PosAtPick = Math.Min(infoRight.PosPrePick, infoC.PosAtPick - safetyDistance - cranes[right].Width / 2);
          infoRight.PosAtDrop = Math.Min(infoRight.PosAtPick, infoC.PosAtDrop - safetyDistance - cranes[right].Width / 2);
          var dhLeft = Math.Abs(infoRight.PosAtPick - infoRight.PosPrePick) / speed;
          var flowLeft = Math.Abs(infoRight.PosAtDrop - infoRight.PosAtPick) / speed;
          infoRight.TimeAtPick = dhLeft + craneInfos[i - 1][right].TimeAtDrop;
          infoRight.TimeAtDrop = infoRight.TimeAtPick + flowLeft;

          safetyDistance += cranes[right].Width;
          if (infoRight.PosAtPick < 0 || infoRight.PosAtPick > world.Width
            || infoRight.PosAtDrop < 0 || infoRight.PosAtDrop > world.Width) infeasible = true;
          craneInfos[i][right] = infoRight;
        }
      }

      if (infeasible) return 100000;
      return craneInfos.Last().Max(x => x.Value.TimeAtDrop); // makespan
    }
    private static double GetLatestTimeOfConflict(IList<(int moveId, int craneId)> sequence, int ith, int craneId, Dictionary<int, CraneInfo>[] info, double[] maxTime, Dictionary<int, IMove> moves, double safetyDistance) {
      var infoI = info[ith - 1][craneId];
      var moveI = moves[sequence[ith].moveId];
      var max = infoI.TimeAtDrop;
      var minPosC = Math.Min(infoI.PosAtPick, infoI.PosAtDrop);
      var maxPosC = Math.Max(infoI.PosAtPick, infoI.PosAtDrop);
      for (var kth = ith - 1; kth > 0; kth--) {
        if (sequence[kth].craneId == craneId) continue; // No need to consider past activities that crane c has already performed, i.e. conflict with itself is last
        foreach (var cx in info[kth]) {
          if (craneId == cx.Key) continue; // crane cannot conflict with itself
          if (sequence[kth].craneId == cx.Key && moveI.PredecessorIds.Contains(sequence[kth].moveId)) {
            max = Math.Max(max, cx.Value.TimeAtDrop);
          } else {
            var infoK = info[kth][cx.Key];
            var minPosX = Math.Min(infoK.PosAtPick, infoK.PosAtDrop);
            var maxPosX = Math.Max(infoK.PosAtPick, infoK.PosAtDrop);

            // TODO: information on the order of cranes is encoded in the id (smaller is to the left of larger)
            if ((craneId < cx.Key && infoI.PosAtPick + safetyDistance < infoK.PosAtPick
                       && infoK.PosAtPick < infoI.PosAtDrop
                       && infoI.PosAtDrop + safetyDistance < infoK.PosAtDrop)
               || (craneId > cx.Key && infoK.PosAtDrop + safetyDistance < infoI.PosAtDrop
                           && infoI.PosAtDrop < infoK.PosAtPick
                           && infoK.PosAtPick + safetyDistance < infoI.PosAtPick)) {
              max = Math.Max(max, infoK.TimeAtPick);
            } else if ((craneId < cx.Key && (maxPosC + safetyDistance > minPosX || minPosC + safetyDistance > maxPosX))
                      || (craneId > cx.Key && (minPosC - safetyDistance < maxPosX || maxPosC - safetyDistance < minPosX))) {
              max = Math.Max(max, infoK.TimeAtDrop);
            }
          }
        }
        if (max >= maxTime[kth]) return max;
      }
      return max;
    }
  }
}
