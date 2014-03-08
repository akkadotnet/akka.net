using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Remote.Transport
{
    public class TcpServer
    {
        private readonly int port;
        private readonly ActorSystem system;
        private string host;
        private TcpListener server;

        public TcpServer(ActorSystem system, string host, int port)
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
            TcpClient client = await server.AcceptTcpClientAsync();
            ProcessSocket(client);
            await Task.Yield();
            WaitForClient();
        }

        private void ProcessSocket(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                while (client.Connected)
                {
                    RemoteEnvelope remoteEnvelope = RemoteEnvelope.ParseDelimitedFrom(stream);
                    object message = MessageSerializer.Deserialize(system, remoteEnvelope.Message);
                    ActorRef recipient = system.Provider.ResolveActorRef(remoteEnvelope.Recipient.Path);
                    ActorRef sender = system.Provider.ResolveActorRef(remoteEnvelope.Sender.Path);

                    recipient.Tell(message, sender);
                }
            }
            catch // (IOException io)
            {
                //    throw;
            }
        }
    }
}