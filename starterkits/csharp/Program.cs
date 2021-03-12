using System;
using System.Text;
using NetMQ;
using NetMQ.Sockets;
using Google.Protobuf;

namespace DynStacking {
  public enum OptimizerType {
    RuleBased,
    ModelBased
  }
  public enum ProblemType {
    HotStorage,
    RollingMill
  }

  interface IPlanner {
    byte[] PlanMoves(byte[] worldData, OptimizerType opt);
  }

  class Program {
    static void Main(string[] args) {
      if (args.Length < 3) {
        Console.WriteLine("Requeries 2 arguments: SOCKET SIM_ID PROBLEM");
        return;
      }
      var socketAddr = args[0];
      var identity = new UTF8Encoding().GetBytes(args[1]);
      IPlanner planner = args[2] == "HS" ? new HotStorage.Planner() : new RollingMill.Planner();

      OptimizerType optType;
      if (args.Length > 2) {
        optType = OptimizerType.ModelBased;
      } else {
        optType = OptimizerType.RuleBased;
      }
      Console.WriteLine(optType);

      using (var socket = new DealerSocket()) {
        socket.Options.Identity = identity;
        socket.Connect(socketAddr);
        Console.WriteLine("Connected");


        while (true) {

          var answer = planner.PlanMoves(socket.ReceiveMultipartBytes()[2], optType);

          if (answer != null) {
            var msg = new NetMQMessage();
            msg.AppendEmptyFrame();
            msg.Append("crane");
            msg.Append(answer);
            socket.SendMultipartMessage(msg);
          }

        }

      }

    }



  }
}
