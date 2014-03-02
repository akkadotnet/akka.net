using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote
{
    public class EndpointActor : UntypedActor
    {
        private Address localAddress;
        private RemoteSettings settings;
        private Transport.Transport transport;
        private Address remoteAddress;

        //TODO: this does not belong here
        private TcpClient client;
        private NetworkStream stream;

        public EndpointActor(Address localAddress,Address remoteAddress,Transport.Transport transport,RemoteSettings settings)
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
            var publicPath = "";
            if (send.Sender is NoSender)
            {
                publicPath = null;
            }
            else if (send.Sender is LocalActorRef)
            {

                var s = send.Sender as LocalActorRef;
                publicPath = send.Sender.Path.ToStringWithAddress(localAddress);
            }
            else
            {
                publicPath = send.Sender.Path.ToString();
            }

            var serializedMessage = MessageSerializer.Serialize(Context.System, send.Message);

            var remoteEnvelope = new RemoteEnvelope.Builder()
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
