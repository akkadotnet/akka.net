using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Remote.Transport
{
    public class TcpServer
    {
        private readonly ActorSystem system;
        private readonly Address localAddress;
        private TcpListener server;
        

        public TcpServer(ActorSystem system, Address localAddress)
        {
            this.system = system;
            this.localAddress = localAddress;
        }

        public void Start()
        {
            try
            {
                server = TcpListener.Create(localAddress.Port.Value);
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
            while (true)
            {
                TcpClient client = await server.AcceptTcpClientAsync();
                ProcessSocket(client);
            }
        }

        private async void ProcessSocket(TcpClient client)
        {
            await Task.Yield();
            try
            {
                NetworkStream stream = client.GetStream();
                while (client.Connected)
                {
                    RemoteEnvelope remoteEnvelope = RemoteEnvelope.ParseDelimitedFrom(stream);

                    Akka.Serialization.Serialization.CurrentTransportInformation = new Information
                    {
                        System = system,
                        Address = localAddress,
                    };

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