//-----------------------------------------------------------------------
// <copyright file="AkkaReader.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// MessagePack-backed reader used by generated Akka.NET serializers.
/// </summary>
public sealed class AkkaReader
{
    private readonly ReadOnlySequence<byte> _buffer;
    private long _consumed;

    public AkkaReader(ReadOnlySequence<byte> buffer)
    {
        _buffer = buffer;
    }

    public long Consumed => _consumed;

    public int BeginReadObject()
    {
        var reader = CreateReader();
        var fieldCount = reader.ReadMapHeader();
        Advance(ref reader);
        return fieldCount;
    }

    public int ReadFieldId()
    {
        return ReadInt32();
    }

    public bool ReadBoolean()
    {
        var reader = CreateReader();
        var value = reader.ReadBoolean();
        Advance(ref reader);
        return value;
    }

    public byte[]? ReadBytes()
    {
        var reader = CreateReader();
        var bytes = reader.ReadBytes();
        Advance(ref reader);
        return bytes?.ToArray();
    }

    public DateTime ReadDateTime()
    {
        var reader = CreateReader();
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTime array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var kind = (DateTimeKind)reader.ReadInt32();
        Advance(ref reader);
        return new DateTime(ticks, kind);
    }

    public DateTimeOffset ReadDateTimeOffset()
    {
        var reader = CreateReader();
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTimeOffset array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt32();
        Advance(ref reader);
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    public decimal ReadDecimal()
    {
        var reader = CreateReader();
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 4)
            throw new MessagePackSerializationException($"Expected decimal array with 4 elements, got {arrayLength}.");

        var lo = reader.ReadInt32();
        var mid = reader.ReadInt32();
        var hi = reader.ReadInt32();
        var flags = reader.ReadInt32();
        Advance(ref reader);
        return new decimal(new[] { lo, mid, hi, flags });
    }

    public double ReadDouble()
    {
        var reader = CreateReader();
        var value = reader.ReadDouble();
        Advance(ref reader);
        return value;
    }

    public Guid ReadGuid()
    {
        var reader = CreateReader();
        var bytes = reader.ReadBytes();
        if (bytes == null || bytes.Value.Length != 16)
            throw new MessagePackSerializationException($"Expected 16 bytes for Guid, got {bytes?.Length ?? 0}.");

        Span<byte> span = stackalloc byte[16];
        bytes.Value.CopyTo(span);
        Advance(ref reader);
        return new Guid(span);
    }

    public int ReadInt32()
    {
        var reader = CreateReader();
        var value = reader.ReadInt32();
        Advance(ref reader);
        return value;
    }

    public long ReadInt64()
    {
        var reader = CreateReader();
        var value = reader.ReadInt64();
        Advance(ref reader);
        return value;
    }

    public string? ReadString()
    {
        var reader = CreateReader();
        var value = reader.ReadString();
        Advance(ref reader);
        return value;
    }

    public void SkipField()
    {
        var reader = CreateReader();
        reader.Skip();
        Advance(ref reader);
    }

    public bool TryReadNil()
    {
        var reader = CreateReader();
        if (!reader.TryReadNil())
            return false;

        Advance(ref reader);
        return true;
    }

    private MessagePackReader CreateReader()
    {
        return new MessagePackReader(_buffer.Slice(_consumed));
    }

    private void Advance(ref MessagePackReader reader)
    {
        _consumed += reader.Consumed;
    }
}
