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
    /// INTERNAL API. Akka.Persistence's serializers, bindings and ids. They register as defaults when the module is deployed; persistence.conf carries no rows for them.
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
