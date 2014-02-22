using Google.ProtocolBuffers;
using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;


namespace Pigeon.Remote
{
    //TODO: rewrite, this should take a RemoteTransport upon creation.
    //move all network related code to RemoteTransport and other remoting classes
    public class RemoteActorRef : MinimalActorRef
    {
        private ActorSystem system;
        protected string actorName;
        private TcpClient client;
        private NetworkStream stream;

        public RemoteActorRef(ActorSystem system, ActorPath remoteActorPath, int port)
        {
            this.system = system;
            this.Path = remoteActorPath;
            this.actorName = this.Path.Name;

            var remoteHostname = remoteActorPath.Address.Host;
            var remotePort = remoteActorPath.Address.Port.Value;
            client = new TcpClient();
            client.Connect(remoteHostname, remotePort);
            stream = client.GetStream();
        }

        protected override void TellInternal(object message, ActorRef sender)
        {           
            var publicPath = "";
            if (sender is LocalActorRef)
            {                
                var s = sender as LocalActorRef;
                publicPath = sender.Path.ToStringWithAddress(system.Provider.Address);
            }
            else
                publicPath = sender.Path.ToString();

            var serializedMessage = MessageSerializer.Serialize(system, message);

            var remoteEnvelope = new RemoteEnvelope.Builder()
            .SetSender(new ActorRefData.Builder()
                .SetPath(publicPath))
            .SetRecipient(new ActorRefData.Builder()
                .SetPath(this.Path.ToStringWithAddress()))
            .SetMessage(serializedMessage)  
            .SetSeq(1)
            .Build();

            Send(remoteEnvelope);
        }

        protected virtual void Send(RemoteEnvelope envelope)
        {
            envelope.WriteDelimitedTo(stream);
            stream.Flush();
        }


        public override ActorRefProvider Provider
        {
            get { return system.Provider; }
        }
    }   
}
