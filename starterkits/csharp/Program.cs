using System;
using System.Text;
using NetMQ;
using NetMQ.Sockets;
using Google.Protobuf;
using DynStacking.HotStorage.DataModel;
using System.Diagnostics;

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
        Console.WriteLine("Requieres 3 arguments: SOCKET SIM_ID PROBLEM");
        return;
      }
      var socketAddr = args[0];
      var identity = new UTF8Encoding().GetBytes(args[1]);
      //IPlanner planner = args[2] == "HS" ? new HotStorage.Planner() : new RollingMill.Planner();
      IPlanner planner = args[2] == "HS" ? new csharp.HS_Sync.SyncHSPlanner() : new RollingMill.Planner();

      OptimizerType optType;
      if (args.Length > 2) {
        optType = OptimizerType.RuleBased;
      } else {
        optType = OptimizerType.ModelBased;
      }
      bool runSynchronously = false;
      if (args.Length >= 4) {
        if (args[3] == "sync") {
          runSynchronously = true;
        } else {
          runSynchronously = false;
        }
      }

      Console.WriteLine(optType);
      if (runSynchronously) {
        Console.WriteLine("Running solver synchronously");
        using (var socket = new ResponseSocket()) {
          socket.Options.Identity = identity;
          socket.Bind(socketAddr);
          Console.WriteLine($"Connected Solver to address {socketAddr}");
          int nextValidStep = 0, currentStep = 0;
          while (true) {
            Console.WriteLine("Listening for Message");
            var request = socket.ReceiveMultipartMessage();
            Console.WriteLine("Message Received");
            var id = request.Pop().ConvertToString();
            if (id != args[1]) {
              socket.SendFrameEmpty(false);
              continue;
            }
            request.Pop(); //empty delim frame
            var skipSteps = Math.Floor((int.Parse(request.Pop().ConvertToString().Split(' ')[1])) / 1000d);

            if (skipSteps > 0 && nextValidStep < currentStep) {
              nextValidStep = currentStep + (int)skipSteps;
            }
            if (nextValidStep == currentStep) {
              var answer = planner.PlanMoves(request.Pop().Buffer, optType);
              if (answer != null) {
                socket.SendFrame(identity, true);
                socket.SendFrameEmpty(true);
                socket.SendFrame($"crane", true);
                socket.SendFrame(answer, false);
              } else {
                socket.SendFrameEmpty(false);
              }
              nextValidStep++;
            } else {
              socket.SendFrameEmpty(false);
            }
            currentStep++;
          }
        }
      } else {
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
}
