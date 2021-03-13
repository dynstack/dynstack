using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DynStack.DataModel.CS;
using DynStack.Simulation.CS;
using NetMQ;
using ProtoBuf;

namespace DynStack.SimulationRunner.CS {
  public class SimulationHost : Common.SimulationHost {
    private CraneSchedulingSimulation sim;
    private bool aborted = false;

    public SimulationHost() { }

    protected override async Task<bool> RunSimulationAsync(byte[] settingsBuf, bool withPolicy = false) {
      var settings = Serializer.Deserialize<World>(settingsBuf.AsSpan());
      var sim = new CraneSchedulingSimulation(settings);
      sim.SetLogger(Logger);

      await sim.RunAsync(TimeSpan.FromMinutes(60));
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
      sim.SetCraneScheduleAsync(schedule);
    }

    private void OnWorldChanged(object sender, EventArgs e) {
      PublishWorldState(((CraneSchedulingSimulation)sender).GetEstimatedWorldState());
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

    public static World DefaultSettings {
      get => WorldBuilder.Build(6, 110, Enumerable.Range(0, 19).Select(x => (pos: 10 + x * 5 + 0.5, maxheight: 5)),
        Enumerable.Range(0, 4).Select(x => (pos: x * 20.0 + 10, width: 1.0)));
    }
  }
}
