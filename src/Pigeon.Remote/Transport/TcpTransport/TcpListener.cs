using Akka.Actor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Transport
{
    public class TcpServer
    {
        private ActorSystem system;
        private int port;
        TcpListener server;
        private string host;

        public TcpServer(ActorSystem system,string host, int port)
        {
            this.system = system;
            this.host = host;
            this.port = port;
        }

        public void Start()
        {
            try
            {
                server = TcpListener.Create(port);
                server.ExclusiveAddressUse = false;
                server.AllowNatTraversal(true);
                server.Start(100);

                WaitForClient();
            }
            catch (Exception x)
            {
                Console.WriteLine(x);
            }
        }

        private async void WaitForClient()
        {            
            var client = await server.AcceptTcpClientAsync();
            ProcessSocket(client);
            await Task.Yield();
            WaitForClient();
        }

        private void ProcessSocket(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                while (client.Connected)
                {
                    var remoteEnvelope = RemoteEnvelope.ParseDelimitedFrom(stream);
                    var message = MessageSerializer.Deserialize(this.system, remoteEnvelope.Message);
                    var recipient = system.Provider.ResolveActorRef(remoteEnvelope.Recipient.Path);
                    var sender = system.Provider.ResolveActorRef(remoteEnvelope.Sender.Path);

                    recipient.Tell(message, sender);
                }
            }
            catch (IOException io)
            {
            //    throw;
            }
        }
    }
}
