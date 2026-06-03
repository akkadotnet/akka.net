//-----------------------------------------------------------------------
// <copyright file="MessagePackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Actor;

namespace Akka.Serialization.V2;

/// <summary>
/// Base class for source-generated MessagePack serializers scoped to a protocol marker type.
/// </summary>
public abstract class MessagePackSerializer<TProtocol> : global::Akka.Serialization.SerializerV2
{
    protected MessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    protected global::Akka.Actor.IActorRef? ReadActorRef(AkkaReader reader)
    {
        var path = reader.ReadString();
        return string.IsNullOrEmpty(path) ? ActorRefs.NoSender : system.Provider.ResolveActorRef(path);
    }

    protected static void WriteActorRef(AkkaWriter writer, global::Akka.Actor.IActorRef? actorRef)
    {
        writer.WriteString(global::Akka.Serialization.Serialization.SerializedActorPath(actorRef));
    }
}
