using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Remote
{
    public class EndpointActor : UntypedActor
    {
        private Address localAddress;
        private RemoteSettings settings;
        private Transport.Transport transport;
        private Address remoteAddress;
        public EndpointActor(Address localAddress,Address remoteAddress,Transport.Transport transport,RemoteSettings settings)
        {
            this.localAddress = localAddress;
            this.remoteAddress = remoteAddress;
            this.transport = transport;
            this.settings = settings;
        }
        protected override void OnReceive(object message)
        {
            throw new NotImplementedException();
        }
    }
}
