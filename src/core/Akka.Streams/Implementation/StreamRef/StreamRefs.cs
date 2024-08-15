// -----------------------------------------------------------------------
//  <copyright file="StreamRefs.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Util.Internal;

namespace Akka.Streams.Implementation.StreamRef;

internal sealed class StreamRefsMaster : IExtension
{
    private readonly EnumerableActorName sinkRefStageNames =
        new EnumerableActorNameImpl("SinkRef", new AtomicCounterLong(0L));

    private readonly EnumerableActorName sourceRefStageNames =
        new EnumerableActorNameImpl("SourceRef", new AtomicCounterLong(0L));

    public StreamRefsMaster(ExtendedActorSystem system)
    {
    }

    public static StreamRefsMaster Get(ActorSystem system)
    {
        return system.WithExtension<StreamRefsMaster, StreamRefsMasterProvider>();
    }

    public string NextSourceRefName()
    {
        return sourceRefStageNames.Next();
    }

    public string NextSinkRefName()
    {
        return sinkRefStageNames.Next();
    }
}

internal sealed class StreamRefsMasterProvider : ExtensionIdProvider<StreamRefsMaster>
{
    public override StreamRefsMaster CreateExtension(ExtendedActorSystem system)
    {
        return new StreamRefsMaster(system);
    }
}