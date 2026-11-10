// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>

#nullable enable
using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization.V2;
using Akka.Streams.Implementation.StreamRef;
using MessagePack;
using AkkaSerialization = Akka.Serialization.Serialization;

namespace Akka.Streams.Serialization;

/// <summary>
/// Formats an Akka Streams sink reference as a single MessagePack actor path.
/// </summary>
/// <typeparam name="T">The stream's element type, supplied by the message schema.</typeparam>
public sealed class SinkRefFormatter<T> : IAkkaMessagePackFormatter<ISinkRef<T>>
{
    private readonly ExtendedActorSystem _system;

    /// <summary>
    /// Creates a formatter that resolves sink references through the receiving actor system.
    /// </summary>
    /// <param name="system">The actor system used to resolve serialized actor paths.</param>
    public SinkRefFormatter(ExtendedActorSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

    /// <inheritdoc />
    public void Write(ref MessagePackWriter writer, ISinkRef<T> value)
    {
        writer.Write(GetPath(value));
    }

    /// <inheritdoc />
    public ISinkRef<T> Read(ref MessagePackReader reader)
    {
        var path = reader.ReadString() ?? throw new SerializationException("Missing sink reference actor path.");
        return new SinkRefImpl<T>(_system.Provider.ResolveActorRef(path));
    }

    /// <inheritdoc />
    public int SizeOf(ISinkRef<T> value)
    {
        return MessagePackSizes.SizeOfString(GetPath(value));
    }

    private static string GetPath(ISinkRef<T> value)
    {
        if (value is not SinkRefImpl<T> sinkRef)
            throw new SerializationException("Expected a non-null Akka Streams sink reference created by StreamRefs.SinkRef<T>().");

        return AkkaSerialization.SerializedActorPath(sinkRef.InitialPartnerRef);
    }
}
