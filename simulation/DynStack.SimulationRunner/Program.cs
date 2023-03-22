using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommandLine;
using DynStack.DataModel.Common;
using NetMQ;

namespace DynStack.SimulationRunner {
  public class Options {
    [Option("id", Required = true, Default = "00000000-0000-0000-0000-000000000000",
      HelpText = "The simulation id that the simulation should identify as.")]
    public string Id { get; set; }
    [Option("sim", Required = true,
      HelpText = "The simulation to start, either HS (=hotstorage), RM (=rollingmill) or CS (=cranescheduling).")]
    public SimulationType SimType { get; set; }
    [Option("url", Default = "tcp://localhost:8080", Required = true, HelpText =
@"The address which is used to communicate with the simulation in asynchronous
mode. World states received from this URL may not be current. Control messages
can be sent at any time. Messages are generally multi-part where the id is in
the first frame, followed by an empty frame, third a frame with a type string
(sim, crane, or world), and fourth a frame with the message body in form of an
appropriate protocol buffer message.")]
    public string URL { get; set; }
    [Option("connect", Default = false, Required = false, HelpText =
@"Whether the runner should attempt to connect to ""url"" or bind to it and
wait for connections. The default is to bind and wait for connections.")]
    public bool Connect { get; set; }
    [Option('p', "policyrun", Default = false, Required = false, HelpText =
@"Whether the simulation should use an integrated policy instead of listening
to the cranecontrol. Policies must implement IPolicy in C#. The simulation
will run in ""virtual time"" when policies are configured. The specific policy
used depends on the specific environment.")]
    public bool PolicyRun { get; set; }
    [Option("settings", Required = false, HelpText =
@"May contain a path to a Settings message stored in protobuf format. When this
option is given, the simulation runs without the start message as soon as it
establishes a connection with the control. If you specify just ""Default"" then
it will run the simulation using default settings.")]
    public string SettingsPath { get; set; }
    [Option("log", Required = false, Default = "Console", HelpText =
@"You can control logging:
""None"" means logging is turned off.
""Console"" (default) means you log to stdout.
Any other input is treated as a filename.")]
    public string Log { get; set; }
    [Option("syncurl", Required = false, HelpText =
@"When a URL is given, e.g., tcp://localhost:8081, then the simulation and policy
run in synchronized mode. This means that the world state that the policy receives
through this address is the latest and does not change while the policy is
calculating. In this mode the simulation runs at the fastest possible pace, only
pausing to await the policies' decisions. Policies have to reply to each world
message with either an empty message or with a message containing action(s).
The message format is multi-part with id in the first frame, followed by empty
frame, a topic, and finally the protocol buffer payload.")]
    public string SyncURL { get; set; }
    public bool RunSync => !string.IsNullOrEmpty(SyncURL);

    [Option("simulateasync", Required = false, Default = false, HelpText =
@"Only allowed together with ""syncurl"" - when the simulation is run in synchronous
mode. With this option, the action from the policy is delayed to appear in the
simulation. The delay is equal to the wall clock time that the policy was measured
to run. In this case, the simulation will not call the policy for every world
update, but only after the measured time has passed in the simulation.
""true"" means the policy's decision are delayed in simulated time equal to the
         amount of wall clock time that the policy took to compute.
""false"" (default) means the policy's decisions are effective immediately.")]
    public bool SimulateAsync { get; set; }
  }

  public class Program {

    static TextWriter Logger = TextWriter.Null;

    static async Task<int> Main(string[] args) {
      try {
        return await Parser.Default.ParseArguments<Options>(args).MapResult(o => Main(o), _ => Task.FromResult(0));
      } catch (Exception e) {
        Logger.WriteLine(e);
        throw;
      } finally {
        NetMQConfig.Cleanup(false);
        Logger.Dispose();
      }
    }

    private static async Task<int> Main(Options o) {
      if (o.SimulateAsync && !o.RunSync && !o.PolicyRun)
        throw new ArgumentException($"The option to simulate asynchronism is only valid in synchronous mode or in a policy run.");
      var cts = new CancellationTokenSource();
      if (o.Log.Equals("console", StringComparison.OrdinalIgnoreCase)) {
        Logger = Console.Out;
        Console.CancelKeyPress += (s, e) => {
          cts.Cancel();
        };
      } else if (!o.Log.Equals("none", StringComparison.OrdinalIgnoreCase)) {
        Logger = File.CreateText(o.Log);
      };
      try {
        switch (o.SimType) {
          case SimulationType.HS:
            if (!await new HS.SimulationHost().RunSimulationAsync(o, Logger, cts.Token))
              return 1;
            break;
          case SimulationType.RM:
            if (!await new RM.SimulationHost().RunSimulationAsync(o, Logger, cts.Token))
              return 1;
            break;
          case SimulationType.CS:
            if (!await new CS.SimulationHost().RunSimulationAsync(o, Logger, cts.Token))
              return 1;
            break;
          default:
            throw new InvalidOperationException("Please specify a valid type of simulation to run.");
        }
      } catch (Exception e) {
        Logger.WriteLine(e);
        return 1;
      }
      return 0;
    }
  }
}
