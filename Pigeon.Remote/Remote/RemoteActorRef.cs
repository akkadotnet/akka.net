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
    public class RemoteActorRef : ActorRef
    {
        private IActorContext Context;
        protected string actorName;
        private TcpClient client;
        private NetworkStream stream;

        public RemoteActorRef(IActorContext context, ActorPath remoteActorPath, int port)
        {
            this.Context = context;
            this.Path = remoteActorPath;

            var remoteHostname = remoteActorPath.Address.Host;
            var remotePort = remoteActorPath.Address.Port.Value;
            client = new TcpClient();
            client.Connect(remoteHostname, remotePort);
            stream = client.GetStream();

            this.Path = remoteActorPath;
            this.Context = context;
           
            this.actorName = this.Path.Name;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            var serializer = Context.System.Serialization.FindSerializerFor(message);
            var messageBytes = serializer.ToBinary(message);
            
            var publicPath = "";
            if (sender is LocalActorRef)
            {                
                var s = sender as LocalActorRef;               
                publicPath = sender.Path.ToStringWithAddress(s.Cell.System.Address);
            }
            else
                publicPath = sender.Path.ToString();

            var messageBuilder = new SerializedMessage.Builder()
                .SetSerializerId(serializer.Identifier);
            if (serializer.IncludeManifest)
                messageBuilder.SetMessageManifest(ByteString.CopyFromUtf8(message.GetType().AssemblyQualifiedName));
            messageBuilder.SetMessage(ByteString.Unsafe.FromBytes(messageBytes));

            var remoteEnvelope = new RemoteEnvelope.Builder()
            .SetSender(new ActorRefData.Builder()
                .SetPath(publicPath))
            .SetRecipient(new ActorRefData.Builder()
                .SetPath(this.Path.ToString()))
            .SetMessage(messageBuilder)  
            .SetSeq(1)
            .Build();

            Send(remoteEnvelope);
        }

        protected virtual void Send(RemoteEnvelope envelope)
        {
            envelope.WriteDelimitedTo(stream);
            stream.Flush();
        }
    }   
}
