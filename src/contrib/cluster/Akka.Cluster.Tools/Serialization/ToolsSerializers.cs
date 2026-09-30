//-----------------------------------------------------------------------
// <copyright file="ToolsSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
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
    /// INTERNAL API. The serializer and binding rows of Akka.Cluster.Tools' three reference.conf files
    /// (Client, PublishSubscribe, Singleton), so they resolve without reflection.
    /// </summary>
    internal sealed class ToolsSerializers : ModuleSerializers
    {
        // the constructor reflection picks for each feature's reference.conf; each class has one constructor, so it always gets that one
        public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
        {
            new ModuleSerializer(typeof(ClusterClientMessageSerializer), (system, _) => new ClusterClientMessageSerializer(system)),
            new ModuleSerializer(typeof(DistributedPubSubMessageSerializer), (system, _) => new DistributedPubSubMessageSerializer(system)),
            new ModuleSerializer(typeof(ClusterSingletonMessageSerializer), (system, _) => new ClusterSingletonMessageSerializer(system)),
        };

        public override IReadOnlyList<Type> BoundTypes { get; } = new[]
        {
            typeof(IClusterClientMessage),
            typeof(IClusterClientProtocolMessage),
            typeof(IDistributedPubSubMessage),
            typeof(SendToOneSubscriber),
            typeof(IClusterSingletonMessage),
        };
    }
}
