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
      HelpText = "The simulation to start, either HS (=hotstorage) or RM (=rollingmill).")]
    public SimulationType SimType { get; set; }
    [Option("url", Default = "tcp://localhost:8080", Required = true, HelpText =
@"The address which is used to communicate with the simulation. One may receive
world updates and send messages to control the simulation or crane. It is
assumed a multi-part message is sent that contains the id in the first frame,
followed by an empty frame, third a frame with a type string (sim, crane, or
world), and fourth a frame with the message body in form of an appropriate
protocol buffer message.")]
    public string URL { get; set; }
    [Option("connect", Default = false, Required = false, HelpText =
@"Whether the runner should attempt to connect to this URL or bind to it and
wait for connections. The default is to bind and wait for connections.")]
    public bool Connect { get; set; }
    [Option('p', "policyrun", Default = false, Required = false, HelpText =
@"Whether the simulation should use policies instead of listening to the
cranecontrol. Policies must be implemented together with the simulation code in
C#. Most likely the simulation will run in ""simulated real time"" when
policies are configured. That is, The result of a policy will be available in
the simulation with a delay equal to the computation time of the policy.
Implementation depends on the concrete simulation.")]
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
        NetMQConfig.Cleanup();
        Logger.Dispose();
      }
    }

    private static async Task<int> Main(Options o) {
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
