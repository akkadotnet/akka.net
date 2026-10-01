//-----------------------------------------------------------------------
// <copyright file="DistributedDataSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.DistributedData.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.DistributedData's reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class DistributedDataSerializers : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-replicated-data", new ReplicatedDataSerializer(system),
                ImmutableHashSet.Create(typeof(IReplicatedDataSerialization))),
            SerializerDetails.Create("akka-data-replication", new ReplicatorMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IReplicatorMessage)))
        );
    }
}
