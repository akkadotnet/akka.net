//-----------------------------------------------------------------------
// <copyright file="PersistenceSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Serialization;

namespace Akka.Persistence.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of persistence.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class PersistenceSerializers : ModuleSerializers
    {
        // the constructor reflection picks for persistence.conf; each class has one constructor, so it always gets that one
        public override IReadOnlyList<BuiltInSerializer> Serializers { get; } = new[]
        {
            new BuiltInSerializer("akka-persistence-message", typeof(PersistenceMessageSerializer), (system, _) => new PersistenceMessageSerializer(system),
                new[] { typeof(IMessage) }),
            new BuiltInSerializer("akka-persistence-snapshot", typeof(PersistenceSnapshotSerializer), (system, _) => new PersistenceSnapshotSerializer(system),
                new[] { typeof(Snapshot) }),
        };
    }
}
