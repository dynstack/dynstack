using System;
using ProtoBuf;

namespace DynStack.DataModel {
  [ProtoContract]
  public struct TimeStamp : IComparable<TimeStamp> {
    [ProtoMember(1)] public long MilliSeconds { get; private set; }

    public TimeStamp(long ms) { MilliSeconds = ms; }
    public int CompareTo(TimeStamp other) => MilliSeconds.CompareTo(other.MilliSeconds);
    public static bool operator <(TimeStamp b, TimeStamp c) => b.MilliSeconds < c.MilliSeconds;
    public static bool operator <=(TimeStamp b, TimeStamp c) => b.MilliSeconds <= c.MilliSeconds;
    public static bool operator >(TimeStamp b, TimeStamp c) => b.MilliSeconds > c.MilliSeconds;
    public static bool operator >=(TimeStamp b, TimeStamp c) => b.MilliSeconds >= c.MilliSeconds;
    public static bool operator ==(TimeStamp b, TimeStamp c) => b.MilliSeconds == c.MilliSeconds;
    public static bool operator !=(TimeStamp b, TimeStamp c) => b.MilliSeconds != c.MilliSeconds;
    public static TimeSpan operator -(TimeStamp b, TimeStamp c) => TimeSpan.FromMilliseconds(b.MilliSeconds - c.MilliSeconds);
    public static TimeStamp operator +(TimeStamp b, TimeSpan c) => new TimeStamp { MilliSeconds = b.MilliSeconds + (long)Math.Round(c.TotalMilliseconds) };
    public static TimeStamp operator -(TimeStamp b, TimeSpan c) => new TimeStamp { MilliSeconds = b.MilliSeconds - (long)Math.Round(c.TotalMilliseconds) };

    public override bool Equals(object obj) {
      if (obj is TimeStamp other)
        return MilliSeconds == other.MilliSeconds;
      return false;
    }
    public override int GetHashCode() {
      return MilliSeconds.GetHashCode();
    }

    public override string ToString() => string.Format("{0:g}", TimeSpan.FromMilliseconds(MilliSeconds));
  }
}
