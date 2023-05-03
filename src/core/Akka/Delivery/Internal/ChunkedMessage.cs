// -----------------------------------------------------------------------
//  <copyright file="ChunkedMessage.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Annotations;
using Akka.IO;

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
        unchecked
        {
            return (EqualityComparer<T?>.Default.GetHashCode(Message) * 397) ^ Chunk.GetHashCode();
        }
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
public readonly struct ChunkedMessage
{
    public ChunkedMessage(ByteString serializedMessage, bool firstChunk, bool lastChunk, int serializerId,
        string manifest)
    {
        SerializedMessage = serializedMessage;
        FirstChunk = firstChunk;
        LastChunk = lastChunk;
        SerializerId = serializerId;
        Manifest = manifest;
    }

    public ByteString SerializedMessage { get; }

    public bool FirstChunk { get; }

    public bool LastChunk { get; }

    public int SerializerId { get; }

    public string Manifest { get; }

    public override string ToString()
    {
        return $"ChunkedMessage({SerializedMessage.Count}, {FirstChunk}, {LastChunk}, {SerializerId}, {Manifest})";
    }
}