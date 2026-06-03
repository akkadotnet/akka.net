//-----------------------------------------------------------------------
// <copyright file="AkkaWriter.cs" company="Akka.NET Project">
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
/// MessagePack-backed writer used by generated Akka.NET serializers.
/// </summary>
public sealed class AkkaWriter
{
    private readonly IBufferWriter<byte> _buffer;
    private readonly CountingBufferWriter _countingBuffer;

    public AkkaWriter(IBufferWriter<byte> buffer)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _countingBuffer = new CountingBufferWriter(_buffer);
    }

    public long BytesWritten => _countingBuffer.BytesWritten;

    public IBufferWriter<byte> RawBuffer => _buffer;

    public void BeginObject(int fieldCount)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.WriteArrayHeader(fieldCount);
        Commit(ref writer);
    }

    public void WriteBoolean(bool value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    public void WriteDateTime(DateTime value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Kind);
        Commit(ref writer);
    }

    public void WriteDateTimeOffset(DateTimeOffset value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Offset.TotalMinutes);
        Commit(ref writer);
    }

    public void WriteDecimal(decimal value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        writer.WriteArrayHeader(4);
        writer.Write(bits[0]);
        writer.Write(bits[1]);
        writer.Write(bits[2]);
        writer.Write(bits[3]);
        Commit(ref writer);
    }

    public void WriteDouble(double value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    public void WriteGuid(Guid value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.WriteBinHeader(16);
        value.TryWriteBytes(writer.GetSpan(16));
        writer.Advance(16);
        Commit(ref writer);
    }

    public void WriteInt32(int value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    public void WriteInt64(long value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    public void WriteNil()
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.WriteNil();
        Commit(ref writer);
    }

    public void WriteString(string? value)
    {
        var writer = new MessagePackWriter(_countingBuffer);
        writer.Write(value);
        Commit(ref writer);
    }

    private void Commit(ref MessagePackWriter writer)
    {
        writer.Flush();
    }

    private sealed class CountingBufferWriter : IBufferWriter<byte>
    {
        private readonly IBufferWriter<byte> _inner;

        public CountingBufferWriter(IBufferWriter<byte> inner)
        {
            _inner = inner;
        }

        public long BytesWritten { get; private set; }

        public void Advance(int count)
        {
            _inner.Advance(count);
            BytesWritten += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            return _inner.GetMemory(sizeHint);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return _inner.GetSpan(sizeHint);
        }
    }
}
