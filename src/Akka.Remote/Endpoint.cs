using System.Net.Sockets;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Remote
{
    public class EndpointActor : UntypedActor
    {
        private readonly Address localAddress;
        private readonly NetworkStream stream;
        private TcpClient client;
        private Address remoteAddress;
        private RemoteSettings settings;
        private Transport.Transport transport;

        public EndpointActor(Address localAddress, Address remoteAddress, Transport.Transport transport,
            RemoteSettings settings)
        {
            this.localAddress = localAddress;
            this.remoteAddress = remoteAddress;
            this.transport = transport;
            this.settings = settings;

            client = new TcpClient();
            client.Connect(remoteAddress.Host, remoteAddress.Port.Value);
            stream = client.GetStream();
        }

        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Send>(Send);
        }


        private void Send(Send send)
        {
            //TODO: should this be here?
            Akka.Serialization.Serialization.CurrentTransportInformation = new Information
            {
                System = Context.System,
                Address = localAddress,
            };

            string publicPath;
            if (send.Sender is NoSender)
            {
                publicPath = "";
            }
            else if (send.Sender is LocalActorRef)
            {
                publicPath = send.Sender.Path.ToStringWithAddress(localAddress);
            }
            else
            {
                publicPath = send.Sender.Path.ToString();
            }

            SerializedMessage serializedMessage = MessageSerializer.Serialize(Context.System, send.Message);

            RemoteEnvelope remoteEnvelope = new RemoteEnvelope.Builder()
                .SetSender(new ActorRefData.Builder()
                    .SetPath(publicPath))
                .SetRecipient(new ActorRefData.Builder()
                    .SetPath(send.Recipient.Path.ToStringWithAddress()))
                .SetMessage(serializedMessage)
                .SetSeq(1)
                .Build();

            remoteEnvelope.WriteDelimitedTo(stream);
            stream.Flush();
        }
    }
}