using System;
using System.Collections.Generic;
using DynStack.DataModel;
using SimSharp;

namespace DynStack.Simulation {
  public interface ILocationResource {
    int Id { get; }
    int MaxHeight { get; }
    int FreeHeight { get; }
    int Height { get; }
    IStack Stack { get; }
    IBlock Topmost { get; }
    double GirderPosition { get; }

    BlockEvent Dropoff(IBlock block);
    StackEvent Dropoff(IStack stack);
    BlockEvent Pickup();
    StackEvent Pickup(int size);

    ITimeSeriesMonitor Utilization { get; }
    ITimeSeriesMonitor WIP { get; }
    ITimeSeriesMonitor DropoffQueueLength { get; }
    ISampleMonitor DropoffWaitingTime { get; }
    ITimeSeriesMonitor PickupQueueLength { get; }
    ISampleMonitor PickupWaitingTime { get; }

    Event WhenNew();
    Event WhenAny();
    Event WhenFull();
    Event WhenEmpty();
    Event WhenChange();
  }

  public class LocationResource : ILocationResource {
    protected IStackingEnvironment World { get; private set; }
    protected ILocation Location { get; private set; }

    public int Id => Location.Id;
    public int MaxHeight => Location.MaxHeight;
    public int Height => Location.Height;
    public int FreeHeight => Location.FreeHeight;
    public IStack Stack => Location.Stack;
    public IBlock Topmost => Location.Topmost;
    public double GirderPosition => Location.GirderPosition;

    protected LinkedList<StackingEvent> DropoffQueue { get; private set; }
    protected LinkedList<StackingEvent> PickupQueue { get; private set; }
    protected List<Event> WhenNewQueue { get; private set; }
    protected List<Event> WhenAnyQueue { get; private set; }
    protected List<Event> WhenFullQueue { get; private set; }
    protected List<Event> WhenEmptyQueue { get; private set; }
    protected List<Event> WhenChangeQueue { get; private set; }

    public ITimeSeriesMonitor Utilization { get; set; }
    public ITimeSeriesMonitor WIP { get; set; }
    public ITimeSeriesMonitor DropoffQueueLength { get; set; }
    public ISampleMonitor DropoffWaitingTime { get; set; }
    public ITimeSeriesMonitor PickupQueueLength { get; set; }
    public ISampleMonitor PickupWaitingTime { get; set; }

    public LocationResource(IStackingEnvironment world, ILocation location) {
      Location = location;
      World = world;
      DropoffQueue = new LinkedList<StackingEvent>();
      PickupQueue = new LinkedList<StackingEvent>();
      WhenNewQueue = new List<Event>();
      WhenAnyQueue = new List<Event>();
      WhenFullQueue = new List<Event>();
      WhenEmptyQueue = new List<Event>();
      WhenChangeQueue = new List<Event>();
    }

    public virtual BlockEvent Dropoff(IBlock block) {
      var dropoff = new BlockEvent(World.Environment, TriggerPickup, CancelDrop) { Block = block };
      DropoffQueue.AddLast(dropoff);
      TriggerDropoff();
      return dropoff;
    }

    public virtual StackEvent Dropoff(IStack stack) {
      var dropoff = new StackEvent(World.Environment, TriggerPickup, CancelDrop) { Stack = stack, Size = stack.Size };
      DropoffQueue.AddLast(dropoff);
      TriggerDropoff();
      return dropoff;
    }

    public virtual BlockEvent Pickup() {
      var pickup = new BlockEvent(World.Environment, TriggerDropoff, CancelPick);
      PickupQueue.AddLast(pickup);
      TriggerPickup();
      return pickup;
    }

    public virtual StackEvent Pickup(int size) {
      var pickup = new StackEvent(World.Environment, TriggerDropoff, CancelPick) { Size = size };
      PickupQueue.AddLast(pickup);
      TriggerPickup();
      return pickup;
    }

    private void CancelDrop(Event e) {
      var se = e as StackingEvent;
      if (se == null) return;
      DropoffQueue.Remove(se);
    }

    private void CancelPick(Event e) {
      var se = e as StackingEvent;
      if (se == null) return;
      PickupQueue.Remove(se);
    }

    public virtual Event WhenNew() {
      var whenNew = new Event(World.Environment);
      WhenNewQueue.Add(whenNew);
      return whenNew;
    }

    public virtual Event WhenAny() {
      var whenAny = new Event(World.Environment);
      WhenAnyQueue.Add(whenAny);
      TriggerWhenAny();
      return whenAny;
    }

    public virtual Event WhenFull() {
      var whenFull = new Event(World.Environment);
      WhenFullQueue.Add(whenFull);
      TriggerWhenFull();
      return whenFull;
    }

    public virtual Event WhenEmpty() {
      var whenEmpty = new Event(World.Environment);
      WhenEmptyQueue.Add(whenEmpty);
      TriggerWhenEmpty();
      return whenEmpty;
    }

    public virtual Event WhenChange() {
      var whenChange = new Event(World.Environment);
      WhenChangeQueue.Add(whenChange);
      return whenChange;
    }

    protected virtual void DoDropoff(StackingEvent dropoff) {
      switch (dropoff) {
        case BlockEvent b:
          if (Location.FreeHeight == 0) return;
          Location.Dropoff(b.Block);
          break;
        case StackEvent s:
          if (Location.FreeHeight < s.Size) return;
          Location.Dropoff(s.Stack);
          break;
        default: throw new InvalidOperationException($"Unknown event type {dropoff?.GetType()}.");
      }
      DropoffWaitingTime?.Add(World.Environment.ToDouble(World.Environment.Now - dropoff.Time));
      dropoff.Succeed();
    }

    protected virtual void DoPickup(StackingEvent @event) {
      switch (@event) {
        case BlockEvent b:
          if (Location.Height == 0) return;
          @event.Succeed(Location.Pickup());
          break;
        case StackEvent s:
          if (Location.Height < s.Size) return;
          @event.Succeed(Location.Pickup(s.Size));
          break;
        default: throw new InvalidOperationException($"Unknown event type {@event?.GetType()}.");
      }
      PickupWaitingTime?.Add(World.Environment.ToDouble(World.Environment.Now - @event.Time));
    }

    protected virtual void TriggerDropoff(Event @event = null) {
      while (DropoffQueue.Count > 0) {
        var put = DropoffQueue.First.Value;
        DoDropoff(put);
        if (put.IsTriggered) {
          DropoffQueue.RemoveFirst();
          TriggerWhenNew();
          TriggerWhenAny();
          TriggerWhenFull();
          TriggerWhenChange();
        } else break;
      }
      Utilization?.UpdateTo(Location.Height / (double)Location.MaxHeight);
      WIP?.UpdateTo(Location.Height + DropoffQueue.Count + PickupQueue.Count);
      DropoffQueueLength?.UpdateTo(DropoffQueue.Count);
    }

    protected virtual void TriggerPickup(Event @event = null) {
      while (PickupQueue.Count > 0) {
        var get = PickupQueue.First.Value;
        DoPickup(get);
        if (get.IsTriggered) {
          PickupQueue.RemoveFirst();
          TriggerWhenEmpty();
          TriggerWhenChange();
        } else break;
      }
      Utilization?.UpdateTo(Location.Height / (double)Location.MaxHeight);
      WIP?.UpdateTo(Location.Height + DropoffQueue.Count + PickupQueue.Count);
      PickupQueueLength?.UpdateTo(PickupQueue.Count);
    }

    protected virtual void TriggerWhenNew() {
      if (WhenNewQueue.Count == 0) return;
      foreach (var evt in WhenNewQueue)
        evt.Succeed();
      WhenNewQueue.Clear();
    }

    protected virtual void TriggerWhenAny() {
      if (Location.Height > 0) {
        if (WhenAnyQueue.Count == 0) return;
        foreach (var evt in WhenAnyQueue)
          evt.Succeed();
        WhenAnyQueue.Clear();
      }
    }

    protected virtual void TriggerWhenFull() {
      if (Location.Height == Location.MaxHeight) {
        if (WhenFullQueue.Count == 0) return;
        foreach (var evt in WhenFullQueue)
          evt.Succeed();
        WhenFullQueue.Clear();
      }
    }

    protected virtual void TriggerWhenEmpty() {
      if (Location.Height == 0) {
        if (WhenEmptyQueue.Count == 0) return;
        foreach (var evt in WhenEmptyQueue)
          evt.Succeed();
        WhenEmptyQueue.Clear();
      }
    }

    protected virtual void TriggerWhenChange() {
      if (WhenChangeQueue.Count == 0) return;
      foreach (var evt in WhenChangeQueue)
        evt.Succeed();
      WhenChangeQueue.Clear();
    }
  }
}
