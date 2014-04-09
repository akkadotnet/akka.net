using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// TCP implementation of a helios transport
    /// </summary>
    class HeliosTcpTransport : HeliosTransport
    {
        public HeliosTcpTransport(ActorSystem system, Config config) : base(system, config)
        {
        }

        public override Task<Tuple<Address, TaskCompletionSource<IAssociationEventListener>>> Listen()
        {
            throw new NotImplementedException();
        }

        public override bool IsResponsibleFor(Address remote)
        {
            throw new NotImplementedException();
        }

        public override Task<AssociationHandle> Associate(Address remoteAddress)
        {
            //throw new NotImplementedException();
        }

        public override Task<bool> Shutdown()
        {
            throw new NotImplementedException();
        }
    }
}