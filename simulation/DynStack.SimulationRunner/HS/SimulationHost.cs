using System;
using System.IO;
using System.Threading.Tasks;
using DynStack.DataModel.HS;
using DynStack.Simulation.HS;
using NetMQ;
using ProtoBuf;

namespace DynStack.SimulationRunner.HS {
  public class SimulationHost : Common.SimulationHost {
    private HotstorageSimulation sim;
    private bool aborted = false;

    public SimulationHost() { }

    protected override async Task<bool> RunSimulationAsync(byte[] settingsBuf, bool withPolicy = false) {
      var settings = Serializer.Deserialize<Settings>(settingsBuf.AsSpan());
      if (withPolicy)
        sim = new HotstorageSimulation(settings, new RuleBasedCranePolicy());
      else sim = new HotstorageSimulation(settings);
      sim.SetLogger(Logger);
      sim.WorldChanged += OnWorldChanged;

      await sim.RunAsync();
      Logger.WriteLine("Run completed");
      return !aborted;
    }

    protected override Task StopAsync() {
      sim.StopAsync();
      aborted = true;
      return Task.CompletedTask;
    }

    protected override async Task OnCraneMessageReceived(byte[] payload) {
      await Task.Delay(200);
      CraneSchedule schedule = null;
      try {
        schedule = Serializer.Deserialize<CraneSchedule>(payload.AsSpan());
      } catch (Exception ex) {
        Logger.WriteLine(ex.ToString());
      }
      if (schedule == null) return;
      await sim.SetCraneScheduleAsync(schedule);
    }

    private void OnWorldChanged(object sender, EventArgs e) {
      PublishWorldState(((HotstorageSimulation)sender).GetEstimatedWorldState());
    }

    private void PublishWorldState(World world) {
      using (var stream = new MemoryStream()) {
        Serializer.Serialize(stream, world);
        var bytes = stream.ToArray();
        var msg = new NetMQMessage();
        msg.AppendEmptyFrame();
        msg.Append("world");
        msg.Append(bytes);
        Outgoing.Enqueue(msg);
      }
    }

    protected override byte[] GetDefaultSettings() {
      using (var stream = new MemoryStream()) {
        var settings = DefaultSettings;
        Serializer.Serialize(stream, settings);
        return stream.ToArray();
      }
    }

    protected override bool RunSimulation(byte[] settingsBuf, string url, string id, bool simulateAsync = true, bool useIntegratedPolicy = false) {
      var settings = Serializer.Deserialize<Settings>(settingsBuf.AsSpan());
      if (useIntegratedPolicy)
        sim = new HotstorageSimulation(settings, new RuleBasedCranePolicy());
      else
        sim = new HotstorageSimulation(settings, new SynchronousSimRunnerPolicy(url, id));

      sim.SetLogger(Logger);
      sim.SimulateAsync = simulateAsync;
      sim.WorldChanged += OnWorldChanged;
      Logger.WriteLine("Starting sim");
      sim.Run();

      return !aborted;
    }

    public static Settings DefaultSettings {
      get => new Settings() {
        BufferCount = 3,
        ProductionMaxHeight = 3,
        BufferMaxHeight = 12,
        CheckInterval = TimeSpan.FromSeconds(.5),
        CraneMoveTimeMean = TimeSpan.FromSeconds(1.5),
        CraneMoveTimeStd = TimeSpan.FromSeconds(.25),
        HoistMoveTimeMean = TimeSpan.FromSeconds(.25),
        HoistMoveTimeStd = TimeSpan.FromSeconds(.05),
        DueTimeMin = TimeSpan.FromSeconds(60),
        InitialNumberOfBlocks = 12,
        Seed = 42,
        ArrivalTimeMean = TimeSpan.FromSeconds(8),
        ArrivalTimeStd = TimeSpan.FromSeconds(1),
        DueTimeStd = TimeSpan.FromSeconds(10),
        DueTimeMean = TimeSpan.FromSeconds(200),
        ReadyFactorMin = 0.1,
        ReadyFactorMax = 0.2,

        MinClearTime = TimeSpan.FromSeconds(0),
        MaxClearTime = TimeSpan.FromSeconds(1),
        HandoverTimeMean = TimeSpan.FromSeconds(4),
        HandoverTimeStd = TimeSpan.FromSeconds(1),

        SimulationDuration = TimeSpan.FromHours(1)
      };
    }
  }
}
