using System;
using System.Collections.Generic;
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
      var settings = Serializer.Deserialize<Settings>(settingsBuf.AsSpan());

      sim = new CraneSchedulingSimulation(settings);
      sim.SetLogger(Logger);
      sim.WorldChanged += Sim_WorldChanged;

      await sim.RunAsync();
      Logger.WriteLine("Run completed");
      return !aborted;
    }

    private void Sim_WorldChanged(object sender, EventArgs e) {
      PublishWorldState(((CraneSchedulingSimulation)sender).GetEstimatedWorldState());
    }

    protected override Task StopAsync() {
      sim.StopAsync();
      aborted = true;
      return Task.CompletedTask;
    }

    protected override async Task OnCraneMessageReceived(byte[] payload) {
      await Task.Delay(200);
      CraneSchedulingSolution sol = null;
      try {
        sol = Serializer.Deserialize<CraneSchedulingSolution>(payload.AsSpan());
      } catch (Exception ex) {
        Logger.WriteLine(ex.ToString());
      }
      if (sol == null) return;
      await sim.SetSolutionAsync(sol);
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

    protected override bool RunSimulation(byte[] settingsBuf, string url, string id, bool simulateAsync = true, bool useIntegratedPolicy = false) {
      throw new NotImplementedException("synchronous mode is not implemented for CS environment.");
    }

    public static Settings DefaultSettings {
      get => new Settings() {
        Seed = 40,
        SimulationDuration = TimeSpan.FromHours(1.0),
        Height = 8,
        Width = 400,
        MaxHeightForArrival = 5,
        MaxHeightForBuffer = 5,
        MaxHeightForHandover = 5,
        ArrivalStackPositions = new List<double> { 40, 50, 60, 240, 250, 260 },
        BufferStackPositions = Enumerable.Range(0, 400 + 1).Select(x => (double)x).ToList(),
        HandoverStackPositions = new List<double> { 140, 150, 160, 340, 350, 360 },
        BlockClasses = 1,
        BufferStackClasses = Enumerable.Repeat(1, 400 + 1).ToList(),
        SafetyDistance = 15.0,
        CraneMoveTimeMean = TimeSpan.FromSeconds(20),
        CraneMoveTimeStd = TimeSpan.FromSeconds(20 * .1),
        HoistMoveTimeMean = TimeSpan.FromSeconds(5),
        HoistMoveTimeStd = TimeSpan.FromSeconds(5 * .1),
        CraneManipulationTimeMean = TimeSpan.FromSeconds(3),
        CraneManipulationTimeStd = TimeSpan.FromSeconds(3 * .1),
        ArrivalTimeMean = TimeSpan.FromMinutes(2),
        ArrivalTimeStd = TimeSpan.FromMinutes(2 * .2),
        ArrivalCountMean = 3.0,
        ArrivalCountStd = 0.75,
        ArrivalServiceTimeMean = TimeSpan.FromSeconds(10),
        ArrivalServiceTimeStd = TimeSpan.FromSeconds(10 * .2),
        HandoverTimeMean = TimeSpan.FromMinutes(2),
        HandoverTimeStd = TimeSpan.FromMinutes(2 * 0.2),
        HandoverCountMean = 3.0,
        HandoverCountStd = 0.75,
        HandoverServiceTimeMean = TimeSpan.FromSeconds(10),
        HandoverServiceTimeStd = TimeSpan.FromSeconds(10 * .2),
        InitialBufferUtilization = 0.25
      };
    }

  }
}
