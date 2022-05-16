using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using DynStack.DataModel.RM;
using DynStack.Simulation.RM;
using NetMQ;
using ProtoBuf;

namespace DynStack.SimulationRunner.RM {
  public class SimulationHost : Common.SimulationHost {
    private RollingMillSimulation sim;
    private bool aborted = false;

    public SimulationHost() { }

    protected override async Task<bool> RunSimulationAsync(byte[] settingsBuf, bool withPolicy = false) {
      var settings = Serializer.Deserialize<Settings>(settingsBuf.AsSpan());
      if (withPolicy)
        sim = new RollingMillSimulation(settings, new BasicSortingPolicyWithHandover());
      else sim = new RollingMillSimulation(settings);
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
      PlannedCraneMoves moves = null;
      try {
        moves = Serializer.Deserialize<PlannedCraneMoves>(payload.AsSpan());
      } catch (Exception ex) {
        Logger.WriteLine(ex.ToString());
      }
      if (moves == null) return;
      await sim.SetPlannedMovesAsync(moves);
    }

    private void OnWorldChanged(object sender, EventArgs e) {
      PublishWorldState(((RollingMillSimulation)sender).GetEstimatedWorldState());
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
      throw new NotImplementedException("synchronous mode is not implemented for RM environment.");
    }

    public static Settings DefaultSettings {
      get => new Settings() {
        ArrivalStackPositions = new List<double> { 1, 2, 3 },
        ShuffleStackPositions = new List<double> { 5, 6, 7 },
        SortedStackPositions = new List<double> { 9, 10, 11, 12 },
        HandoverStackPositions = new Dictionary<MillTypes, double> {
          { MillTypes.A, 14 },
          { MillTypes.B, 15 }
        },
        Width = 16,
        MaxHeightAtHandover = 4,
        MaxHeightForArrival = 4,
        MaxHeightForShuffleBuffer = 6,
        MaxHeightForSortedBuffer = 8,
        CraneCapacity = 4,
        Height = 10,
        ArrivalTimeMean = TimeSpan.FromSeconds(90),
        ArrivalTimeStd = TimeSpan.FromSeconds(18),
        CraneMoveTimeMean = TimeSpan.FromSeconds(10),
        CraneMoveTimeStd = TimeSpan.FromSeconds(2),
        HoistMoveTimeMean = TimeSpan.FromSeconds(3),
        HoistMoveTimeStd = TimeSpan.FromSeconds(.5),
        CraneManipulationTimeMean = TimeSpan.FromSeconds(5),
        CraneManipulationTimeStd = TimeSpan.FromSeconds(1),
        SafetyDistance = 1,
        Seed = 42,
        InitialPhase = TimeSpan.FromMinutes(20),
        SimulationDuration = TimeSpan.FromHours(1),
        ProgramSizeMin = 10,
        ProgramSizeMax = 20,
        ProgramBlockIntervalMin = TimeSpan.FromSeconds(30),
        ProgramBlockIntervalMax = TimeSpan.FromSeconds(40),
        ProgramIntervalMin = TimeSpan.FromSeconds(60),
        ProgramIntervalMax = TimeSpan.FromSeconds(80),
        ArrivalSequencePoolSize = 10,
        ProgramCount = 4,
        ArrivalLotSizeWeights = new List<double> { 0.1, 0.2, 0.4, 0.3 },
        ArrivalMillPurity = 0.5,
        ArrivalUnloadTimeMin = TimeSpan.FromSeconds(2),
        ArrivalUnloadTimeMax = TimeSpan.FromSeconds(5),
      };
    }
  }
}
