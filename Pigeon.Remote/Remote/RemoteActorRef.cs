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
    public abstract class JsonRemoteActorRef : ActorRef
    {
        private IActorContext Context;
        public string URL { get; private set; }
        protected string actorName;

        public JsonRemoteActorRef(IActorContext context, ActorPath remoteActorPath,int port)
        {
            this.Path = remoteActorPath;
            this.Context = context;
            var tmp = this.Path.ToString().Substring(0, this.Path.ToString().Length - this.Path.Name.Length);
            URL = tmp.Substring("pigeon.".Length);
           
            this.actorName = this.Path.Name;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            var messageBody = SerializeMessageToString(message);

            var publicPath = "";
            if (sender is LocalActorRef)
                publicPath = string.Format("{0}{1}", this.Context.System.GetSystemName(), sender.Path.ToString());
            else
                publicPath = sender.Path.ToString();

            var remoteEnvelope = new RemoteEnvelope.Builder()
            .SetSender(new ActorRefData.Builder()
                .SetPath(publicPath))
            .SetRecipient(new ActorRefData.Builder()
                .SetPath(this.Path.ToString()))
            .SetMessage(new SerializedMessage.Builder()
                .SetSerializerId(1)
                .SetMessage(ByteString.CopyFrom(messageBody, Encoding.Default)))
            .SetSeq(1)
            .Build();

            Send(remoteEnvelope);
        }

        protected virtual string SerializeMessageToString(object message)
        {
            var messageBody = fastJSON.JSON.Instance.ToJSON(message);
            return messageBody;
        }

        protected abstract void Send(RemoteEnvelope envelope);
    }
    public class RemoteActorRef : JsonRemoteActorRef
    {
        private ActorCell actorCell;

        private TcpClient client;
        private NetworkStream stream;
        public RemoteActorRef(ActorCell actorCell, ActorPath actorPath,int port) : base(actorCell,actorPath,port)
        {
            this.actorCell = actorCell;
            this.Path = actorPath;

            var remoteHostname = actorPath.GetHostName();
            var remotePort = actorPath.GetPort(); 
            client = new TcpClient();            
            client.Connect(remoteHostname, remotePort);
            stream = client.GetStream();
        }       

        protected override void Send(RemoteEnvelope envelope)
        {
            envelope.WriteDelimitedTo(stream);
            stream.Flush();
        }
    }
}
