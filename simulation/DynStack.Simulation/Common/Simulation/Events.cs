using System;
using System.Collections.Generic;
using System.Linq;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {
  public abstract class StackingEvent : Event {
    private Action<Event> _cancel;

    public IEnumerable<IBlock> Blocks {
      get {
        switch (Value) {
          case IBlock b: return new IBlock[] { b };
          case IStack s: return s.BottomToTop;
          default: return Enumerable.Empty<IBlock>();
        }
      }
    }

    public DateTime Time { get; private set; }
    protected StackingEvent(SimSharp.Simulation environment, Action<Event> callback, Action<Event> cancel) : base(environment) {
      CallbackList.Add(callback);
      Time = environment.Now;
      _cancel = cancel;
    }
    public override void Succeed(object value = null, int priority = 0) {
      if (value == null && Value == null) throw new InvalidOperationException("Succeed without value.");
      base.Succeed(value ?? Value, priority);
    }

    public void Cancel() {
      _cancel?.Invoke(this);
    }
  }

  public class BlockEvent : StackingEvent {
    public IBlock Block { get => (IBlock)Value; set => Value = value; }
    public BlockEvent(SimSharp.Simulation environment, Action<Event> callback, Action<Event> cancel) : base(environment, callback, cancel) { }
  }

  public class StackEvent : StackingEvent {
    public int Size { get; set; }
    public IStack Stack { get => (IStack)Value; set => Value = value; }
    public StackEvent(SimSharp.Simulation environment, Action<Event> callback, Action<Event> cancel) : base(environment, callback, cancel) { }
  }

  /// <summary>
  /// Countdowns are like timeouts, but do not immediately trigger. Instead, they're triggered
  /// when a callback is added, often that is when a process yields them.
  /// </summary>
  public sealed class Countdown : Event {
    public TimeSpan Delay { get; private set; }
    public int Priority { get; private set; }
    /// <summary>
    /// A countdown is an event that is executed after a certain timespan has passed AFTER a
    /// callback that has been added. E.g. a process yields this event and then the countdown
    /// starts.
    /// </summary>
    /// <remarks>
    /// Countdown events are not triggered upon creation, but become triggered as soon as
    /// a callback is added to them.
    /// </remarks>
    /// <param name="environment">The environment in which it is scheduled.</param>
    /// <param name="delay">The timespan for the countdown.</param>
    /// <param name="value">The value of the countdown.</param>
    /// <param name="isOk">Whether the countdown should succeed or fail.</param>
    /// <param name="priority">The priority to rank events at the same time (smaller value = higher priority).</param>
    public Countdown(SimSharp.Simulation environment, TimeSpan delay, object value = null, bool isOk = true, int priority = 0)
      : base(environment) {
      IsOk = isOk;
      Value = value;
      Delay = delay;
      Priority = priority;
    }

    public override void AddCallback(Action<Event> callback) {
      if (IsAlive) {
        Environment.Schedule(Delay, this, Priority);
        IsTriggered = true;
      }
      base.AddCallback(callback);
    }

    public override void AddCallbacks(IEnumerable<Action<Event>> callbacks) {
      if (IsAlive) {
        Environment.Schedule(Delay, this, Priority);
        IsTriggered = true;
      }
      base.AddCallbacks(callbacks);
    }
  }

  public class ZoneRequest : Request {
    public double LowerPosition { get; private set; }
    public double HigherPosition { get; private set; }

    protected Action<Event> ShrinkCallback { get; private set; }

    public ZoneRequest(IStackingEnvironment environment, Action<Event> callback, Action<Event> disposeCallback, double lower, double higher)
      : base(environment.Environment, callback, disposeCallback) {
      LowerPosition = lower;
      HigherPosition = higher;
    }
  }
}
