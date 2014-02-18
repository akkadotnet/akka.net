using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote.Transport
{
    public class ProtocolTransportAddressPair
    {
        public ProtocolTransportAddressPair(AkkaProtocolTransport protocolTransport,Address address)
        {
            this.ProtocolTransport = protocolTransport;
            this.Address = address;
        }

        public AkkaProtocolTransport ProtocolTransport { get;private set; }

        public Address Address { get; private set; }
    }
    public class AkkaProtocolTransport
    {
    }
}
