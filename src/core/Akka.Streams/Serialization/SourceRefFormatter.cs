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
/// Formats an Akka Streams source reference as a single MessagePack actor path.
/// </summary>
/// <typeparam name="T">The stream's element type, supplied by the message schema.</typeparam>
public sealed class SourceRefFormatter<T> : IAkkaMessagePackFormatter<ISourceRef<T>>
{
    private readonly ExtendedActorSystem _system;

    /// <summary>
    /// Creates a formatter that resolves source references through the receiving actor system.
    /// </summary>
    /// <param name="system">The actor system used to resolve serialized actor paths.</param>
    public SourceRefFormatter(ExtendedActorSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        _system = system;
    }

    /// <inheritdoc />
    public void Write(ref MessagePackWriter writer, ISourceRef<T> value)
    {
        writer.Write(GetPath(value));
    }

    /// <inheritdoc />
    public ISourceRef<T> Read(ref MessagePackReader reader)
    {
        var path = reader.ReadString() ?? throw new SerializationException("Missing source reference actor path.");
        return new SourceRefImpl<T>(_system.Provider.ResolveActorRef(path));
    }

    /// <inheritdoc />
    public int SizeOf(ISourceRef<T> value)
    {
        return MessagePackSizes.SizeOfString(GetPath(value));
    }

    private static string GetPath(ISourceRef<T> value)
    {
        if (value is not SourceRefImpl<T> sourceRef)
            throw new SerializationException("Expected a non-null Akka Streams source reference created by StreamRefs.SourceRef<T>().");

        return AkkaSerialization.SerializedActorPath(sourceRef.InitialPartnerRef);
    }
}
