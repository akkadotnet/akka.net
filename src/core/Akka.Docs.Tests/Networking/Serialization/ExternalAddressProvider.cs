//-----------------------------------------------------------------------
// <copyright file="ExternalAddressProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Util.Internal;

namespace DocsExamples.Networking.Serialization
{
    #region external-address-extension
    public class ExternalAddress : ExtensionIdProvider<ExternalAddressExtension>
    {
        public override ExternalAddressExtension CreateExtension(ExtendedActorSystem system) => new(system);
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
    #endregion

    public class ExternalAddressUsage
    {
        private ExtendedActorSystem ExtendedSystem =>
            ActorSystem.Create("test").AsInstanceOf<ExtendedActorSystem>();

        #region serialize-with-external-address
        public string SerializeTo(IActorRef actorRef, Address remote)
        {
            return actorRef.Path.ToSerializationFormatWithAddress(
                new ExternalAddress().Get(ExtendedSystem).AddressFor(remote));
        }
        #endregion
    }
}
