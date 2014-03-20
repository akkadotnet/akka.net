using Akka.Actor;

namespace Akka.Remote.Transport
{
    public class ProtocolTransportAddressPair
    {
        public ProtocolTransportAddressPair(AkkaProtocolTransport protocolTransport, Address address)
        {
            ProtocolTransport = protocolTransport;
            Address = address;
        }

        public AkkaProtocolTransport ProtocolTransport { get; private set; }

        public Address Address { get; private set; }
    }

    public class AkkaProtocolTransport
    {
        private ActorSystem actorSystem;
        private AkkaProtocolSettings akkaProtocolSettings;

        public AkkaProtocolTransport(Transport wrappedTransport, ActorSystem actorSystem,
            AkkaProtocolSettings akkaProtocolSettings)
        {
            Transport = wrappedTransport;
            this.actorSystem = actorSystem;
            this.akkaProtocolSettings = akkaProtocolSettings;
        }

        public Transport Transport { get; private set; }
    }
}