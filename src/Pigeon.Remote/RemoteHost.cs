using Pigeon.Actor;
using Pigeon.Remote;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{    
    public class RemoteHost 
    {
        private ActorSystem system;
        private int port;
        TcpListener server;
        public static RemoteHost StartHost(ActorSystem system,int port)
        {
            var host = new RemoteHost(system,port);
                           
            return host;
        }

        public RemoteHost(ActorSystem system,int port)
        {
            try
            {
                this.system = system;
                this.port = port;

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
