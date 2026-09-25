//-----------------------------------------------------------------------
// <copyright file="StreamsSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;

namespace Akka.Streams.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.Streams' reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class StreamsSerializers : ModuleSerializers
    {
        // the constructor reflection picks for reference.conf; StreamRefSerializer has one, so it always gets that one
        public override IReadOnlyList<ModuleSerializer> Serializers { get; } = new[]
        {
            new ModuleSerializer(typeof(StreamRefSerializer), (system, _) => new StreamRefSerializer(system)),
        };

        public override IReadOnlyList<Type> BoundTypes { get; } = new[]
        {
            typeof(SinkRefImpl),
            typeof(SourceRefImpl),
            typeof(IStreamRefsProtocol),
        };
    }
}
