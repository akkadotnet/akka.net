//-----------------------------------------------------------------------
// <copyright file="SourceRefSurrogate.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util;

namespace Akka.Streams.Serialization
{
    /// <summary>
    /// INTERNAL API
    ///
    /// Allows <see cref="ISourceRef{TOut}"/> to be safely serialized and deserialized during POCO serialization.
    /// </summary>
    internal sealed class SourceRefSurrogate : ISurrogate
    {
        public SourceRefSurrogate(string eventType, string originPath)
        {
            EventType = eventType;
            OriginPath = originPath;
        }

        public string EventType { get; }
        public string OriginPath { get; }

        public ISurrogated FromSurrogate(ActorSystem system) =>
            SerializationTools.ToSourceRefImpl((ExtendedActorSystem) system, EventType, OriginPath);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Allows <see cref="ISinkRef{TOut}"/> to be safely serialized and deserialized during POCO serialization.
    /// </summary>
    internal sealed class SinkRefSurrogate : ISurrogate
    {
        public SinkRefSurrogate(string eventType, string originPath)
        {
            EventType = eventType;
            OriginPath = originPath;
        }

        public string EventType { get; }
        public string OriginPath { get; }

        public ISurrogated FromSurrogate(ActorSystem system)
        {
            return SerializationTools.ToSinkRefImpl((ExtendedActorSystem)system, EventType, OriginPath);
        }
    }
}
