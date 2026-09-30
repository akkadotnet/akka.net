//-----------------------------------------------------------------------
// <copyright file="ClusterSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Cluster.Routing;
using Akka.Delivery.Internal;
using Akka.Serialization;

namespace Akka.Cluster.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Cluster.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class ClusterSerializers : ModuleSerializers
    {
        // the constructor reflection picks for Cluster.conf; each class has one constructor, so it always gets that one
        public override IReadOnlyList<SerializerRegistration> Serializers { get; } = new[]
        {
            new SerializerRegistration("akka-cluster", typeof(ClusterMessageSerializer), (system, _) => new ClusterMessageSerializer(system),
                new[] { typeof(IClusterMessage), typeof(ClusterRouterPool) }),
            new SerializerRegistration("reliable-delivery", typeof(ReliableDeliverySerializer), (system, _) => new ReliableDeliverySerializer(system),
                new[] { typeof(IDeliverySerializable) }),
        };
    }
}
