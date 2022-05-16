using DynStack.DataModel.HS;
using NetMQ;
using NetMQ.Sockets;
using ProtoBuf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DynStack.Simulation.HS {
  public class SynchronousSimRunnerPolicy : IPolicy {

    private string url = "";
    private Guid simId = Guid.Empty;

    public SynchronousSimRunnerPolicy(string url, string id) {
      this.url = url;
      simId = Guid.Parse(id);
      requestSocket = new DealerSocket();
      requestSocket.Bind(url);
    }

    ~SynchronousSimRunnerPolicy() {
      if (!requestSocket.IsDisposed)
        requestSocket.Dispose();
    }

    private INetMQSocket requestSocket;

    public CraneSchedule GetSchedule(World world) {
      using (var worldStream = new MemoryStream()) {
        requestSocket.Options.Identity = new UTF8Encoding().GetBytes(simId.ToString());

        Serializer.Serialize(worldStream, world);
        var bytes = worldStream.ToArray();

        int retriesLeft = 3;

        while (retriesLeft > 0) {
          requestSocket.SendFrameEmpty(true);
          requestSocket.SendFrame($"skip {world.SimSleep}", true);
          requestSocket.SendFrame(bytes, false);

          NetMQMessage answer = new NetMQMessage();
          var gotAnswer = requestSocket.TryReceiveMultipartMessage(TimeSpan.FromSeconds(1.5), ref answer);

          if (gotAnswer) {
            answer.Pop(); // empty delim frame
            if (answer.Pop().ConvertToString() == "crane") {
              var next = answer.Pop();
              if (next.BufferSize == 0)
                return null;

              var scheduleStream = new MemoryStream(next.Buffer);
              var returnValue = Serializer.Deserialize<CraneSchedule>(scheduleStream);
              scheduleStream.Close();
              return returnValue;
            }
          } else {
            Console.WriteLine("No answer received, retrying...");
            retriesLeft--;
          }
        }
        Console.WriteLine($"No retries left, returning empty");
        return null;
      }
    }
  }
}
