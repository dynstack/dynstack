using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace Simulation.Util {
  public static class Extensions {
    public static TimeStamp NowTS(this SimSharp.Simulation sim) {
      var ms = Math.Round((sim.Now - sim.StartDate).TotalMilliseconds);
      return new TimeStamp((long)ms);
    }
    public static TimeStamp ToTimeStamp(this SimSharp.Simulation sim, DateTime date) {
      var ms = Math.Round((sim.Now - sim.StartDate).TotalMilliseconds);
      return new TimeStamp((long)ms);
    }

    public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, IRandom random) {
      T[] elements = source.ToArray();
      for (int i = elements.Length - 1; i > 0; i--) {
        // Swap element "i" with a random earlier element (including itself)
        int swapIndex = random.Next(i + 1);
        yield return elements[swapIndex];
        elements[swapIndex] = elements[i];
        // we don't actually perform the swap, we can forget about the
        // swapped element because we already returned it.
      }
      if (elements.Length > 0)
        yield return elements[0];
    }

    public static IEnumerable<T> SampleProportional<T>(this IEnumerable<T> source, IRandom random, IEnumerable<double> weights, bool windowing, bool inverseProportional) {
      var sourceArray = source.ToArray();
      var valueArray = PrepareProportional(weights, windowing, inverseProportional);
      double total = valueArray.Sum();

      while (true) {
        int index = 0;
        double ball = valueArray[index], sum = random.NextDouble() * total;
        while (ball < sum)
          ball += valueArray[++index];
        yield return sourceArray[index];
      }
    }

    public static IEnumerable<T> SampleProportionalWithoutRepetition<T>(this IEnumerable<T> source, IRandom random, IEnumerable<double> weights, bool windowing, bool inverseProportional) {
      var valueArray = PrepareProportional(weights, windowing, inverseProportional);
      var list = new LinkedList<Tuple<T, double>>(source.Zip(valueArray, Tuple.Create));
      double total = valueArray.Sum();

      while (list.Count > 0) {
        var cur = list.First;
        double ball = cur.Value.Item2, sum = random.NextDouble() * total; // assert: sum < total. When there is only one item remaining: sum < ball
        while (ball < sum && cur.Next != null) {
          cur = cur.Next;
          ball += cur.Value.Item2;
        }
        yield return cur.Value.Item1;
        list.Remove(cur);
        total -= cur.Value.Item2;
      }
    }

    private static double[] PrepareProportional(IEnumerable<double> weights, bool windowing, bool inverseProportional) {
      double maxValue = double.MinValue, minValue = double.MaxValue;
      double[] valueArray = weights.ToArray();

      for (int i = 0; i < valueArray.Length; i++) {
        if (valueArray[i] > maxValue) maxValue = valueArray[i];
        if (valueArray[i] < minValue) minValue = valueArray[i];
      }
      if (minValue == maxValue) {  // all values are equal
        for (int i = 0; i < valueArray.Length; i++) {
          valueArray[i] = 1.0;
        }
      } else {
        if (windowing) {
          if (inverseProportional) InverseProportionalScale(valueArray, maxValue);
          else ProportionalScale(valueArray, minValue);
        } else {
          if (minValue < 0.0) throw new InvalidOperationException("Proportional selection without windowing does not work with values < 0.");
          if (inverseProportional) InverseProportionalScale(valueArray, 2 * maxValue);
        }
      }
      return valueArray;
    }
    private static void ProportionalScale(double[] values, double minValue) {
      for (int i = 0; i < values.Length; i++) {
        values[i] = values[i] - minValue;
      }
    }
    private static void InverseProportionalScale(double[] values, double maxValue) {
      for (int i = 0; i < values.Length; i++) {
        values[i] = maxValue - values[i];
      }
    }
  }
}
