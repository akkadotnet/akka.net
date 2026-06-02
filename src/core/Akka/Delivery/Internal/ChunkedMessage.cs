//-----------------------------------------------------------------------
// <copyright file="ChunkedMessage.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Annotations;

namespace Akka.Delivery.Internal;

[InternalApi]
public readonly struct MessageOrChunk<T> : IEquatable<MessageOrChunk<T>>
{
    public MessageOrChunk(T message)
    {
        Message = message;
        Chunk = null;
    }

    public MessageOrChunk(ChunkedMessage chunkedMessage)
    {
        Message = default;
        Chunk = chunkedMessage;
    }

    public T? Message { get; }

    public ChunkedMessage? Chunk { get; }

    public bool IsMessage => Message != null;

    public static implicit operator MessageOrChunk<T>(T message)
    {
        return new MessageOrChunk<T>(message);
    }

    public static implicit operator T(MessageOrChunk<T> message)
    {
        return message.IsMessage
            ? message.Message!
            : throw new InvalidCastException(
                $"MessageOrChunk<{typeof(T).Name}> is a ChunkedMessage and is not castable to [{typeof(T)}].");
    }

    public static implicit operator ChunkedMessage(MessageOrChunk<T> message)
    {
        return message.IsMessage
            ? throw new InvalidCastException(
                $"MessageOrChunk<{typeof(T).Name}> is a [{typeof(T)}] and is not castable to ChunkedMessage.")
            : message.Chunk!.Value;
    }

    public static implicit operator MessageOrChunk<T>(ChunkedMessage chunkedMessage)
    {
        return new MessageOrChunk<T>(chunkedMessage);
    }

    public bool Equals(MessageOrChunk<T> other)
    {
        return EqualityComparer<T?>.Default.Equals(Message, other.Message) && Nullable.Equals(Chunk, other.Chunk);
    }

    public override bool Equals(object? obj)
    {
        if (obj is null)
            return false;

        switch (obj)
        {
            case MessageOrChunk<T> other:
                return Equals(other);
            case T msg when IsMessage:
                return EqualityComparer<T>.Default.Equals(Message!, msg);
            case ChunkedMessage chunk when !IsMessage:
                return Chunk!.Equals(chunk);
            default:
                return false;
        }
    }

    public override int GetHashCode()
    {
        return IsMessage ? Message!.GetHashCode() : Chunk!.GetHashCode();
    }

    public override string ToString()
    {
        return IsMessage ? $"Message: {Message}" : $"Chunk: {Chunk}";
    }
}

/// <summary>
///     INTERNAL API
///     Used for segments of large messages during point-to-point delivery.
/// </summary>
[InternalApi]
public readonly struct ChunkedMessage : IEquatable<ChunkedMessage>
{
    public ChunkedMessage(ReadOnlyMemory<byte> serializedMessage, bool firstChunk, bool lastChunk, int serializerId,
        string manifest)
    {
        SerializedMessage = serializedMessage;
        FirstChunk = firstChunk;
        LastChunk = lastChunk;
        SerializerId = serializerId;
        Manifest = manifest;
    }

    public ReadOnlyMemory<byte> SerializedMessage { get; }

    public bool FirstChunk { get; }

    public bool LastChunk { get; }

    public int SerializerId { get; }

    public string Manifest { get; }

    public bool Equals(ChunkedMessage other)
    {
        return SerializedMessage.Span.SequenceEqual(other.SerializedMessage.Span)
               && FirstChunk == other.FirstChunk
               && LastChunk == other.LastChunk
               && SerializerId == other.SerializerId
               && Manifest == other.Manifest;
    }

    public override bool Equals(object? obj)
    {
        return obj is ChunkedMessage other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = FirstChunk.GetHashCode();
            hash = (hash * 397) ^ LastChunk.GetHashCode();
            hash = (hash * 397) ^ SerializerId;
            hash = (hash * 397) ^ (Manifest?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ SerializedMessage.Length;
            return hash;
        }
    }

    public static bool operator ==(ChunkedMessage left, ChunkedMessage right) => left.Equals(right);
    public static bool operator !=(ChunkedMessage left, ChunkedMessage right) => !left.Equals(right);

    public override string ToString()
    {
        return $"ChunkedMessage({SerializedMessage.Length}, {FirstChunk}, {LastChunk}, {SerializerId}, {Manifest})";
    }
}
