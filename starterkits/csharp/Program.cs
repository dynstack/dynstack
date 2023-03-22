using NetMQ;
using NetMQ.Sockets;
using System;
using System.Text;

namespace DynStacking {
  public enum OptimizerType {
    RuleBased,
    ModelBased
  }
  public enum ProblemType {
    HotStorage,
    RollingMill,
    CraneScheduling
  }

  interface IPlanner {
    byte[] PlanMoves(byte[] worldData, OptimizerType opt);
  }

  class Program {
    static void Main(string[] args) {
      if (args.Length < 3) {
        Console.WriteLine("Requires 3 arguments: SOCKET SIM_ID PROBLEM");
        return;
      }
      var socketAddr = args[0];
      var identity = new UTF8Encoding().GetBytes(args[1]);
      //IPlanner planner = args[2] == "HS" ? new HotStorage.Planner() : new RollingMill.Planner();
      IPlanner planner = args[2] switch {
        "HS" => new csharp.HS_Sync.SyncHSPlanner(),
        "RM" => new RollingMill.Planner(),
        "CS" => new CraneScheduling.Planner(),
        _ => null
      };

      if (planner == null) {
        Console.WriteLine($"Invalid problem: {args[2]}");
        return;
      }

      OptimizerType optType;
      if (args.Length > 2) {
        optType = OptimizerType.RuleBased;
      } else {
        optType = OptimizerType.ModelBased;
      }

      Console.WriteLine(optType);

      using (var socket = new DealerSocket()) {
        socket.Options.Identity = identity;
        socket.Connect(socketAddr);
        Console.WriteLine("Connected");

        while (true) {
          Console.WriteLine("Waiting for request...");
          var request = socket.ReceiveMultipartBytes();
          Console.WriteLine("Incoming request");
          var answer = planner.PlanMoves(request[2], optType);

          var msg = new NetMQMessage();
          msg.AppendEmptyFrame();
          msg.Append("crane");
          if (answer != null)
            msg.Append(answer);
          else
            msg.AppendEmptyFrame();

          socket.SendMultipartMessage(msg);
        }
      }
    }
  }
}
