//-----------------------------------------------------------------------
// <copyright file="AkkaSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Globalization;
using System.Runtime.Serialization;
using Akka.Actor;
using MessagePack;

namespace Akka.Serialization.V2;

/// <summary>
/// Base class for source-generated MessagePack serializers scoped to a protocol marker type.
/// </summary>
public abstract class AkkaSerializer : SerializerV2
{
    /// <summary>
    /// Maximum depth of nested envelope payloads (properties whose static type is <c>object</c> or
    /// <c>object?</c>) permitted within a single serialize / deserialize / size operation. Envelopes
    /// legitimately nest a level or two (a
    /// delivery message wraps a user payload that may itself be an enveloped message), but unbounded
    /// nesting is only reachable when a message type declares itself (directly or transitively) as its own
    /// envelope payload — an application bug. Left unchecked that recurses until the thread's stack
    /// overflows, which in .NET is an uncatchable process kill. Past this depth we throw an ordinary,
    /// catchable <see cref="SerializationException"/> instead, matching the recursion limit
    /// <c>Google.Protobuf</c> already enforces. This is a robustness guard against accidental self-nesting,
    /// not a security control: Akka remoting carries one application's own traffic between its own nodes.
    /// </summary>
    private const int MaxEnvelopePayloadDepth = 100;

    [ThreadStatic] private static int _envelopePayloadDepth;

    private static void EnterEnvelopePayload()
    {
        if (_envelopePayloadDepth >= MaxEnvelopePayloadDepth)
            throw new SerializationException(
                $"Envelope payload nesting exceeded the maximum depth of {MaxEnvelopePayloadDepth}. " +
                "This almost always means a message type declares an object-typed envelope field that (directly " +
                "or transitively) holds itself, causing unbounded recursion. Break the self-reference in the message graph.");

        _envelopePayloadDepth++;
    }

    private static void ExitEnvelopePayload() => _envelopePayloadDepth--;

    protected AkkaSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    /// <inheritdoc />
    public override byte[] ToBinary(object obj)
    {
        var writer = new ArrayBufferWriter<byte>();
        Serialize(obj, writer);
        return writer.WrittenMemory.ToArray();
    }

    protected IActorRef? ReadActorRef(ref MessagePackReader reader)
    {
        var path = reader.ReadString();
        return string.IsNullOrEmpty(path) ? ActorRefs.NoSender : system.Provider.ResolveActorRef(path);
    }

    protected static void WriteActorRef(ref MessagePackWriter writer, IActorRef? actorRef)
    {
        writer.Write(Serialization.SerializedActorPath(actorRef));
    }

    protected void WriteEnvelopePayload(ref MessagePackWriter writer, object? payload)
    {
        if (payload is null)
        {
            writer.WriteNil();
            return;
        }

        var serializer = system.Serialization.FindSerializerFor(payload);
        var manifest = Serialization.ManifestFor(serializer, payload);

        if (serializer is SerializerV2 serializerV2)
        {
            using var buffer = new AkkaPooledBufferWriter();
            int bytesWritten;
            EnterEnvelopePayload();
            try
            {
                bytesWritten = serializerV2.Serialize(payload, buffer);
            }
            finally
            {
                ExitEnvelopePayload();
            }

            if (bytesWritten != buffer.WrittenCount)
                throw new SerializationException(
                    $"Serializer [{serializer.GetType()}] reported [{bytesWritten}] bytes but wrote [{buffer.WrittenCount}] bytes.");

            writer.WriteMapHeader(3);
            writer.Write(1);
            writer.Write(serializer.Identifier);
            writer.Write(2);
            writer.Write(manifest);
            writer.Write(3);
            WriteBytes(ref writer, buffer.WrittenSpan);
        }
        else
        {
            var bytes = serializer.ToBinary(payload);
            writer.WriteMapHeader(3);
            writer.Write(1);
            writer.Write(serializer.Identifier);
            writer.Write(2);
            writer.Write(manifest);
            writer.Write(3);
            WriteBytes(ref writer, bytes);
        }
    }

    protected object? ReadEnvelopePayload(ref MessagePackReader reader)
    {
        if (reader.TryReadNil())
            return null;

        var fieldCount = reader.ReadMapHeader();
        int? serializerId = null;
        var manifest = string.Empty;
        ReadOnlySequence<byte>? bytes = null;

        for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)
        {
            var fieldId = reader.ReadInt32();
            switch (fieldId)
            {
                case 1:
                    serializerId = reader.ReadInt32();
                    break;
                case 2:
                    manifest = reader.ReadString() ?? string.Empty;
                    break;
                case 3:
                    bytes = reader.ReadBytes();
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (serializerId is null)
            throw new SerializationException("Missing envelope payload serializer id.");
        if (bytes is null)
            throw new SerializationException("Missing envelope payload bytes.");

        EnterEnvelopePayload();
        try
        {
            return system.Serialization.Deserialize(bytes.Value, serializerId.Value, manifest);
        }
        finally
        {
            ExitEnvelopePayload();
        }
    }

    protected int SizeOfEnvelopePayload(object? payload)
    {
        if (payload is null)
            return SizeOfNil();

        var serializer = system.Serialization.FindSerializerFor(payload);
        if (serializer is not SerializerV2 serializerV2)
            return SerializerV2.UnknownSize;

        int payloadSize;
        EnterEnvelopePayload();
        try
        {
            payloadSize = serializerV2.SizeHint(payload);
        }
        finally
        {
            ExitEnvelopePayload();
        }

        if (payloadSize < 0)
            return SerializerV2.UnknownSize;

        var manifest = Serialization.ManifestFor(serializer, payload);
        return checked(
            SizeOfMapHeader(3) +
            SizeOfInt32(1) + SizeOfInt32(serializer.Identifier) +
            SizeOfInt32(2) + SizeOfString(manifest) +
            SizeOfInt32(3) + SizeOfBinHeader(payloadSize) + payloadSize);
    }

    protected static int SizeOfNil() => MessagePackSizes.SizeOfNil();

    protected static int SizeOfBoolean(bool value) => MessagePackSizes.SizeOfBoolean(value);

    protected static int SizeOfDouble(double value) => MessagePackSizes.SizeOfDouble(value);

    protected static int SizeOfInt32(int value) => MessagePackSizes.SizeOfInt32(value);

    protected static int SizeOfInt64(long value) => MessagePackSizes.SizeOfInt64(value);

    protected static int SizeOfUInt64(ulong value) => MessagePackSizes.SizeOfUInt64(value);

    protected static int SizeOfSingle(float value) => MessagePackSizes.SizeOfSingle(value);

    protected static int SizeOfEnum(int value) => MessagePackSizes.SizeOfEnum(value);

    protected static int SizeOfMapHeader(int count) => MessagePackSizes.SizeOfMapHeader(count);

    protected static int SizeOfArrayHeader(int count) => MessagePackSizes.SizeOfArrayHeader(count);

    protected static int SizeOfString(string? value) => MessagePackSizes.SizeOfString(value);

    protected static int SizeOfBytes(byte[]? value) => MessagePackSizes.SizeOfBytes(value);

    protected static int SizeOfGuid(Guid value) => MessagePackSizes.SizeOfGuid(value);

    protected static int SizeOfDateTime(DateTime value) => MessagePackSizes.SizeOfDateTime(value);

    protected static int SizeOfDateTimeOffset(DateTimeOffset value) => MessagePackSizes.SizeOfDateTimeOffset(value);

    protected static int SizeOfDecimal(decimal value) => MessagePackSizes.SizeOfDecimal(value);

    protected static int SizeOfActorRef(IActorRef? actorRef) => MessagePackSizes.SizeOfActorRef(actorRef);

    protected static int SizeOfBinHeader(int byteCount) => MessagePackSizes.SizeOfBinHeader(byteCount);

    private sealed class AkkaPooledBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer;
        private int _written;

        public AkkaPooledBufferWriter()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(256);
        }

        public int WrittenCount => _written;

        public ReadOnlySpan<byte> WrittenSpan => new(_buffer, 0, _written);

        public void Advance(int count)
        {
            if (count < 0 || _written > _buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));

            _written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Dispose()
        {
            var buffer = _buffer;
            _buffer = Array.Empty<byte>();
            _written = 0;
            if (buffer.Length > 0)
                ArrayPool<byte>.Shared.Return(buffer);
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint < 0)
                throw new ArgumentOutOfRangeException(nameof(sizeHint));

            if (sizeHint == 0)
                sizeHint = 1;

            if (sizeHint <= _buffer.Length - _written)
                return;

            var newSize = Math.Max(_buffer.Length * 2, _written + sizeHint);
            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            _buffer.AsSpan(0, _written).CopyTo(newBuffer);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }
    }

    protected TPayload ReadEnvelopePayload<TPayload>(ref MessagePackReader reader)
    {
        var payload = ReadEnvelopePayload(ref reader);
        if (payload is TPayload typed)
            return typed;

        throw new SerializationException(
            $"Envelope payload [{payload?.GetType().FullName ?? "<null>"}] is not assignable to [{typeof(TPayload).FullName}].");
    }

    private static void WriteBytes(ref MessagePackWriter writer, ReadOnlySpan<byte> bytes)
    {
        writer.WriteBinHeader(bytes.Length);
        bytes.CopyTo(writer.GetSpan(bytes.Length));
        writer.Advance(bytes.Length);
    }

    protected static DateTime ReadDateTime(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTime array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var kind = (DateTimeKind)reader.ReadInt32();
        return new DateTime(ticks, kind);
    }

    protected static void WriteDateTime(ref MessagePackWriter writer, DateTime value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Kind);
    }

    protected static DateTimeOffset ReadDateTimeOffset(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new MessagePackSerializationException($"Expected DateTimeOffset array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt32();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    protected static void WriteDateTimeOffset(ref MessagePackWriter writer, DateTimeOffset value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Offset.TotalMinutes);
    }

    protected static decimal ReadDecimal(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 4)
            throw new MessagePackSerializationException($"Expected decimal array with 4 elements, got {arrayLength}.");

        var lo = reader.ReadInt32();
        var mid = reader.ReadInt32();
        var hi = reader.ReadInt32();
        var flags = reader.ReadInt32();
        return new decimal(new[] { lo, mid, hi, flags });
    }

    protected static void WriteDecimal(ref MessagePackWriter writer, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        writer.WriteArrayHeader(4);
        writer.Write(bits[0]);
        writer.Write(bits[1]);
        writer.Write(bits[2]);
        writer.Write(bits[3]);
    }

    protected static Guid ReadGuid(ref MessagePackReader reader)
    {
        var bytes = reader.ReadBytes();
        if (bytes == null || bytes.Value.Length != 16)
            throw new MessagePackSerializationException($"Expected 16 bytes for Guid, got {bytes?.Length ?? 0}.");

        Span<byte> span = stackalloc byte[16];
        bytes.Value.CopyTo(span);
        return new Guid(span);
    }

    protected static void WriteGuid(ref MessagePackWriter writer, Guid value)
    {
        writer.WriteBinHeader(16);
        value.TryWriteBytes(writer.GetSpan(16));
        writer.Advance(16);
    }

    // ---------------------------------------------------------------------------------------------
    // Small-scalar readers (short, sbyte, byte, ushort, uint, ulong, float, char).
    //
    // Writers need no helper: the generator calls MessagePackWriter.Write(...) directly, which picks
    // the smallest MessagePack integer encoding for the value, exactly as it does for int and long.
    // Readers DO need one: the wire carries "an integer" with no width, so a value written by a wider
    // peer (or a corrupt/hostile frame) can exceed the target type. Every reader below range-checks
    // and throws a MessagePackSerializationException naming the target type, never truncating.
    // ---------------------------------------------------------------------------------------------

    private static long ReadSignedInteger(ref MessagePackReader reader, string targetType)
    {
        try
        {
            return reader.ReadInt64();
        }
        catch (OverflowException ex)
        {
            throw new MessagePackSerializationException(
                $"The integer on the wire does not fit in a signed 64-bit integer, so it cannot be read as [{targetType}].", ex);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new MessagePackSerializationException($"The value on the wire cannot be read as [{targetType}]: {ex.Message}", ex);
        }
    }

    private static ulong ReadUnsignedInteger(ref MessagePackReader reader, string targetType)
    {
        try
        {
            return reader.ReadUInt64();
        }
        catch (OverflowException ex)
        {
            throw new MessagePackSerializationException(
                $"The integer on the wire is negative or too large for an unsigned 64-bit integer, so it cannot be read as [{targetType}].", ex);
        }
        catch (MessagePackSerializationException ex)
        {
            throw new MessagePackSerializationException($"The value on the wire cannot be read as [{targetType}]: {ex.Message}", ex);
        }
    }

    private static MessagePackSerializationException OutOfRange(string targetType, string value, string range)
        => new($"The integer [{value}] on the wire is outside the range of [{targetType}] ({range}).");

    /// <summary>
    /// Reads a <see cref="TimeSpan"/> written as the signed integer of its <see cref="TimeSpan.Ticks"/>.
    /// Every int64 is a valid tick count, so only a value beyond int64 (or a non-integer) is rejected.
    /// </summary>
    protected static TimeSpan ReadTimeSpan(ref MessagePackReader reader)
        => new(ReadSignedInteger(ref reader, "System.TimeSpan"));

    protected static short ReadInt16(ref MessagePackReader reader)
    {
        var value = ReadSignedInteger(ref reader, "System.Int16");
        if (value < short.MinValue || value > short.MaxValue)
            throw OutOfRange("System.Int16", value.ToString(CultureInfo.InvariantCulture), $"{short.MinValue} to {short.MaxValue}");

        return (short)value;
    }

    protected static sbyte ReadSByte(ref MessagePackReader reader)
    {
        var value = ReadSignedInteger(ref reader, "System.SByte");
        if (value < sbyte.MinValue || value > sbyte.MaxValue)
            throw OutOfRange("System.SByte", value.ToString(CultureInfo.InvariantCulture), $"{sbyte.MinValue} to {sbyte.MaxValue}");

        return (sbyte)value;
    }

    protected static byte ReadByte(ref MessagePackReader reader)
    {
        var value = ReadUnsignedInteger(ref reader, "System.Byte");
        if (value > byte.MaxValue)
            throw OutOfRange("System.Byte", value.ToString(CultureInfo.InvariantCulture), $"0 to {byte.MaxValue}");

        return (byte)value;
    }

    protected static ushort ReadUInt16(ref MessagePackReader reader)
    {
        var value = ReadUnsignedInteger(ref reader, "System.UInt16");
        if (value > ushort.MaxValue)
            throw OutOfRange("System.UInt16", value.ToString(CultureInfo.InvariantCulture), $"0 to {ushort.MaxValue}");

        return (ushort)value;
    }

    protected static uint ReadUInt32(ref MessagePackReader reader)
    {
        var value = ReadUnsignedInteger(ref reader, "System.UInt32");
        if (value > uint.MaxValue)
            throw OutOfRange("System.UInt32", value.ToString(CultureInfo.InvariantCulture), $"0 to {uint.MaxValue}");

        return (uint)value;
    }

    protected static ulong ReadUInt64(ref MessagePackReader reader)
        => ReadUnsignedInteger(ref reader, "System.UInt64");

    /// <summary>
    /// Reads a <see cref="char"/> written as the unsigned integer of its UTF-16 code unit.
    /// </summary>
    protected static char ReadChar(ref MessagePackReader reader)
    {
        var value = ReadUnsignedInteger(ref reader, "System.Char");
        if (value > char.MaxValue)
            throw OutOfRange("System.Char", value.ToString(CultureInfo.InvariantCulture), $"0 to {(int)char.MaxValue}");

        return (char)value;
    }

    /// <summary>
    /// Reads a <see cref="float"/>. MessagePack float32 (what the generator writes) is read exactly;
    /// a float64 on the wire is accepted only when it fits in a float32, so a too-large double is
    /// rejected instead of silently becoming infinity.
    /// </summary>
    protected static float ReadSingle(ref MessagePackReader reader)
    {
        double wide;
        try
        {
            wide = reader.ReadDouble();
        }
        catch (MessagePackSerializationException ex)
        {
            throw new MessagePackSerializationException($"The value on the wire cannot be read as [System.Single]: {ex.Message}", ex);
        }

        var narrow = (float)wide;
        if (float.IsInfinity(narrow) && !double.IsInfinity(wide))
            throw new MessagePackSerializationException(
                $"The floating-point value [{wide.ToString("R", CultureInfo.InvariantCulture)}] on the wire is outside the range of [System.Single].");

        return narrow;
    }
}
