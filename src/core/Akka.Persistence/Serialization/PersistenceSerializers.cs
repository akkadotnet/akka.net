//-----------------------------------------------------------------------
// <copyright file="PersistenceSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of persistence.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class PersistenceSerializers : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-persistence-message", new PersistenceMessageSerializer(system),
                ImmutableHashSet.Create(typeof(IMessage))),
            SerializerDetails.Create("akka-persistence-snapshot", new PersistenceSnapshotSerializer(system),
                ImmutableHashSet.Create(typeof(Snapshot)))
        );
    }
}
