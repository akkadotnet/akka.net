//-----------------------------------------------------------------------
// <copyright file="DistributedDataSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Serialization;

namespace Akka.DistributedData.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.DistributedData's reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class DistributedDataSerializers : ModuleSerializers
    {
        // the constructor reflection picks for reference.conf; each class has one constructor, so it always gets that one
        public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
        {
            new ModuleSerializer(typeof(ReplicatedDataSerializer), (system, _) => new ReplicatedDataSerializer(system)),
            new ModuleSerializer(typeof(ReplicatorMessageSerializer), (system, _) => new ReplicatorMessageSerializer(system)),
        };

        public override IReadOnlyList<Type> BoundTypes { get; } = new[]
        {
            typeof(IReplicatedDataSerialization),
            typeof(IReplicatorMessage),
        };
    }
}
