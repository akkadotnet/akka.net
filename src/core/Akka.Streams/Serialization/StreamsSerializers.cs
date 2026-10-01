//-----------------------------------------------------------------------
// <copyright file="StreamsSerializers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Serialization;
using Akka.Streams.Implementation.StreamRef;

namespace Akka.Streams.Serialization
{
    /// <summary>
    /// INTERNAL API. The serializer and binding rows of Akka.Streams' reference.conf, so they resolve without reflection.
    /// </summary>
    internal sealed class StreamsSerializers : ModuleSerializers
    {
        public override ImmutableHashSet<SerializerDetails> Create(ExtendedActorSystem system) => ImmutableHashSet.Create(
            SerializerDetails.Create("akka-stream-ref", new StreamRefSerializer(system),
                ImmutableHashSet.Create(typeof(SinkRefImpl), typeof(SourceRefImpl), typeof(IStreamRefsProtocol)))
        );
    }
}
