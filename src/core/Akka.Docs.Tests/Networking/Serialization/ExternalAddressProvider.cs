using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace DocsExamples.Networking.Serialization
{
    public class ExternalAddress : ExtensionIdProvider<ExternalAddressExtension>
    {
        public override ExternalAddressExtension CreateExtension(ExtendedActorSystem system) =>
            new ExternalAddressExtension(system);
    }

    public class ExternalAddressExtension : IExtension
    {
        private readonly ExtendedActorSystem _system;

        public ExternalAddressExtension(ExtendedActorSystem system)
        {
            _system = system;
        }

        public Address AddressFor(Address remoteAddr)
        {
            return _system.Provider.GetExternalAddressFor(remoteAddr) 
                ?? throw new InvalidOperationException($"cannot send to {remoteAddr}");
        }
    }

    public class Test
    {
        private ExtendedActorSystem ExtendedSystem =>
            ActorSystem.Create("test").AsInstanceOf<ExtendedActorSystem>();

        public string SerializeTo(IActorRef actorRef, Address remote)
        {
            return actorRef.Path.ToSerializationFormatWithAddress(
                new ExternalAddress().Get(ExtendedSystem).AddressFor(remote));
        }
    }
}
