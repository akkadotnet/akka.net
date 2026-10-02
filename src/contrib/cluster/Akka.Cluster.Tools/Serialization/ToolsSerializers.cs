//-----------------------------------------------------------------------
// <copyright file="ToolsSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Tools.Client;
using Akka.Cluster.Tools.Client.Serialization;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Cluster.Tools.PublishSubscribe.Serialization;
using Akka.Cluster.Tools.Singleton;
using Akka.Cluster.Tools.Singleton.Serialization;
using Akka.Serialization;

namespace Akka.Cluster.Tools
{
    /// <summary>
    /// INTERNAL API. The serializers, bindings and ids of Akka.Cluster.Tools' Client, PublishSubscribe and
    /// Singleton. They register as defaults when the module is deployed; the three reference.conf files carry no
    /// rows for them.
    /// </summary>
    internal sealed class ToolsSerializers : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-cluster-client", new ClusterClientMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IClusterClientMessage), typeof(IClusterClientProtocolMessage))),
            SerializerDetails.Create("akka-pubsub", new DistributedPubSubMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IDistributedPubSubMessage), typeof(SendToOneSubscriber))),
            SerializerDetails.Create("akka-singleton", new ClusterSingletonMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IClusterSingletonMessage)))
        );
    }
}
