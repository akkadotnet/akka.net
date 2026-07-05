//-----------------------------------------------------------------------
// <copyright file="ActorPathFormatter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Runtime.Serialization;
using Akka.Actor;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// Built-in <see cref="IAkkaMessagePackFormatter{T}"/> for <see cref="ActorPath"/>.
/// </summary>
/// <remarks>
/// Writes a single transport-aware MessagePack string, mirroring how the generator already
/// serializes <see cref="IActorRef"/> fields: when a transport context is active (that is, when
/// serialization is happening underneath <c>Akka.Serialization.Serialization.WithTransport</c>,
/// as it is for every path that goes through <c>ActorSystem.Serialization</c>), the path is
/// rendered with that transport's address via <see cref="ActorPath.ToSerializationFormatWithAddress"/>;
/// otherwise it falls back to <see cref="ActorPath.ToSerializationFormat"/>.
/// </remarks>
public sealed class ActorPathFormatter : IAkkaMessagePackFormatter<ActorPath>
{
    /// <inheritdoc />
    public void Write(ref MessagePackWriter writer, ActorPath value)
    {
        writer.Write(GetSerializationFormat(value));
    }

    /// <inheritdoc />
    public ActorPath Read(ref MessagePackReader reader)
    {
        var path = reader.ReadString() ?? throw new SerializationException("Missing actor path.");
        return ActorPath.Parse(path);
    }

    /// <inheritdoc />
    public int SizeOf(ActorPath value)
    {
        return MessagePackSizes.SizeOfString(GetSerializationFormat(value));
    }

    private static string GetSerializationFormat(ActorPath value)
    {
        var info = global::Akka.Serialization.Serialization.CurrentTransportInformation;
        return info is not null ? value.ToSerializationFormatWithAddress(info.Address) : value.ToSerializationFormat();
    }
}
