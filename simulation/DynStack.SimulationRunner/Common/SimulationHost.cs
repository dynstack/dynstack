using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DynStack.DataModel.Messages;
using NetMQ;
using NetMQ.Sockets;
using ProtoBuf;

namespace DynStack.SimulationRunner.Common {
  public abstract class SimulationHost {
    private readonly TaskCompletionSource<byte[]> _settingsReceived = new TaskCompletionSource<byte[]>();
    private DealerSocket _socket;
    protected NetMQQueue<NetMQMessage> Outgoing { get; private set; }

    protected string Id { get; private set; }

    protected TextWriter Logger { get; private set; }

    public async Task<bool> RunSimulationAsync(Options options, TextWriter logger, CancellationToken token = default) {
      Logger = logger;
      try {
        Id = options.Id;

        _socket = new DealerSocket();
        _socket.Options.Linger = TimeSpan.FromSeconds(30);
        _socket.Options.Identity = Encoding.ASCII.GetBytes(Id);
        if (options.Connect) _socket.Connect(options.URL);
        else _socket.Bind(options.URL);

        using (Outgoing = new NetMQQueue<NetMQMessage>()) {
          using var poller = new NetMQPoller() { _socket, Outgoing };
          _socket.ReceiveReady += (sender, e) => {
            var msg = _socket.ReceiveMultipartMessage();
            if (msg.FrameCount < 3) {
              logger.WriteLine($"Received message with only {msg.FrameCount} frames.");
              return;
            }
            var type = msg[1].ConvertToString();
            logger.WriteLine($"Received {type} message.");
            switch (type) {
              case "crane":
                OnCraneMessageReceived(msg[2].Buffer);
                break;
              case "sim":
                OnSimMessageReceived(msg[2].Buffer);
                break;
              default: //Console.WriteLine($"Received message with unmapped type {type}");
                break;
            }
          };
          Outgoing.ReceiveReady += (sender, e) => {
            var msg = Outgoing.Dequeue();
            if (_socket.TrySendMultipartMessage(TimeSpan.FromMinutes(1), msg)) // Note that for policyruns (virtual time) a lot of events are generated
              logger.WriteLine($"Sent {msg[1].ConvertToString()} message.");
            //else logger.WriteLine($"Discarded outgoing {msg[1].ConvertToString()} message.");
          };

          if (!options.RunSync || options.Connect)
            poller.RunAsync();

          if (!string.IsNullOrEmpty(options.SettingsPath)) {
            if (options.SettingsPath.Equals("Default", StringComparison.OrdinalIgnoreCase)) {
              logger.WriteLine("Using default settings");
              _settingsReceived.SetResult(GetDefaultSettings());
            } else {
              logger.WriteLine($"Reading settings from {options.SettingsPath}");
              _settingsReceived.SetResult(File.ReadAllBytes(options.SettingsPath));
            }
          }
          var result = false;
          try {
            if (!options.RunSync) {
              result = await RunSimulationAsync(await _settingsReceived.Task, options.PolicyRun);
              // wait until the outgoing queue is cleared
              var remaining = Outgoing.Count;
              while (remaining > 0) {
                await Task.Delay(1000, token);
                if (Outgoing.Count == remaining) break; // assume nobody is listening for world states
                remaining = Outgoing.Count;
              }
            } else {
              result = RunSimulation(await _settingsReceived.Task, options.SyncURL, options.Id);
            }
          } finally {
            poller.Stop();
          }
          return result;
        }
      } finally {
        DisposeSocket();
      }
    }

    private void OnSimMessageReceived(byte[] payload) {
      SimControl control = null;
      try {
        control = Serializer.Deserialize<SimControl>(payload.AsSpan());
      } catch (Exception ex) {
        Logger.WriteLine(ex.ToString());
      }
      if (control == null) {
        return;
      }
      if (control.Id != Id) {
        Logger.WriteLine($"Ignoring SimControl message for wrong id: {control.Id} instead of {Id}");
        return;
      }
      if (control.Action == SimControl.START_SIM) {
        try {
          if (!_settingsReceived.Task.IsCompleted)
            _settingsReceived.SetResult(control.Settings);
        } catch { } // ignore potential race-condition
        return;
      }
      if (control.Action == SimControl.STOP_SIM) {
        StopAsync();
      }
    }

    protected abstract Task OnCraneMessageReceived(byte[] payload);

    protected abstract bool RunSimulation(byte[] settingsBuf, string url, string id, bool simulateAsync = true, bool useIntegratedPolicy = false);

    protected abstract Task<bool> RunSimulationAsync(byte[] settingsBuf, bool withPolicy);
    protected abstract Task StopAsync();

    protected abstract byte[] GetDefaultSettings();

    private void DisposeSocket() {
      try { _socket?.Dispose(); } catch { }
    }
  }
}
