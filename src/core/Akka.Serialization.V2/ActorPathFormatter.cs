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
/// <para>
/// Writes a single transport-aware MessagePack string, mirroring how the generator already
/// serializes <see cref="IActorRef"/> fields. Address resolution order:
/// </para>
/// <list type="number">
/// <item><description>
/// the thread-static transport context (<c>Serialization.CurrentTransportInformation</c>, read
/// directly with a null check — no exception is thrown or caught on the non-transport path),
/// which is set whenever serialization runs underneath <c>Akka.Serialization.Serialization</c>
/// (as it does for everything that goes through <c>ActorSystem.Serialization</c>);
/// </description></item>
/// <item><description>
/// otherwise, when this formatter was constructed with an <see cref="ExtendedActorSystem"/> —
/// which generated serializers do automatically, since the generator prefers the
/// <see cref="ExtendedActorSystem"/> constructor — the system's <c>Provider.DefaultAddress</c>,
/// so paths written outside any transport scope are still remotely resolvable, matching
/// <c>Serialization.SerializedActorPath</c> semantics;
/// </description></item>
/// <item><description>
/// otherwise <see cref="ActorPath.ToSerializationFormat"/> (address as-is).
/// </description></item>
/// </list>
/// <para>
/// This formatter is transport-sensitive: <see cref="SizeOf"/> and <see cref="Write"/> each read
/// the thread-static transport context independently, so both must run under the same transport
/// scope on the same thread for the exact-size contract to hold. The generated serializers and
/// the Artery encode path do this naturally (size and write happen within one serialization
/// scope); only callers that hand-roll sizing on a different thread or scope can tear.
/// </para>
/// </remarks>
public sealed class ActorPathFormatter : IAkkaMessagePackFormatter<ActorPath>
{
    private readonly ExtendedActorSystem? _system;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorPathFormatter"/> class without system
    /// context: outside a transport scope, paths are written via
    /// <see cref="ActorPath.ToSerializationFormat"/> (address as-is).
    /// </summary>
    public ActorPathFormatter()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ActorPathFormatter"/> class with system
    /// context: outside a transport scope, paths are written with the system's default address
    /// (<c>Provider.DefaultAddress</c>) so they remain remotely resolvable. Generated serializers
    /// use this constructor automatically.
    /// </summary>
    /// <param name="system">The actor system whose default address anchors non-transport writes.</param>
    public ActorPathFormatter(ExtendedActorSystem system)
    {
        _system = system;
    }

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

    private string GetSerializationFormat(ActorPath value)
    {
        var info = global::Akka.Serialization.Serialization.CurrentTransportInformation;
        if (info is not null)
            return value.ToSerializationFormatWithAddress(info.Address);

        if (_system is not null)
            return value.ToSerializationFormatWithAddress(_system.Provider.DefaultAddress);

        return value.ToSerializationFormat();
    }
}
