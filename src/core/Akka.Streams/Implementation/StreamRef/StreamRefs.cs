//-----------------------------------------------------------------------
// <copyright file="StreamRefs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.StreamRef
{
    internal sealed class StreamRefsMaster : IExtension
    {
        public static StreamRefsMaster Get(ActorSystem system) =>
            system.WithExtension<StreamRefsMaster, StreamRefsMasterProvider>();

        private readonly EnumerableActorName sourceRefStageNames = new EnumerableActorNameImpl("SourceRef", new AtomicCounterLong(0L));
        private readonly EnumerableActorName sinkRefStageNames = new EnumerableActorNameImpl("SinkRef", new AtomicCounterLong(0L));

        public StreamRefsMaster(ExtendedActorSystem system)
        {

        }

        public string NextSourceRefName() => sourceRefStageNames.Next();
        public string NextSinkRefName() => sinkRefStageNames.Next();
    }

    internal sealed class StreamRefsMasterProvider : ExtensionIdProvider<StreamRefsMaster>
    {
        public override StreamRefsMaster CreateExtension(ExtendedActorSystem system) =>
            new StreamRefsMaster(system);
    }
}
