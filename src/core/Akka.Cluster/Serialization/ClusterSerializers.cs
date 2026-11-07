//-----------------------------------------------------------------------
// <copyright file="ClusterSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Routing;
using Akka.Delivery.Internal;
using Akka.Serialization;

namespace Akka.Cluster.Serialization
{
    /// <summary>
    /// INTERNAL API. Akka.Cluster's serializers, bindings and ids. They register as defaults when the module is deployed; Cluster.conf carries no rows for them.
    /// </summary>
    internal sealed class ClusterSerializers : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-cluster", new ClusterMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IClusterMessage), typeof(ClusterRouterPool))),
            SerializerDetails.Create("reliable-delivery", new ReliableDeliverySerializer(system),
                ImmutableHashSet.Create(typeof(IDeliverySerializable)))
        );
    }
}
