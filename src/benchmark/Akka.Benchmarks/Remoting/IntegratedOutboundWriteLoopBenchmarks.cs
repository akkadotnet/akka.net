//-----------------------------------------------------------------------
// <copyright file="IntegratedOutboundWriteLoopBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Text;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Remote.Serialization;
using Akka.Remote.Serialization.Proto.Msg;
using Akka.Remote.Transport;
using Akka.Serialization;
using Akka.Util;
using BenchmarkDotNet.Attributes;
using Google.Protobuf;

namespace Akka.Benchmarks.Remoting;

[Config(typeof(MicroBenchmarkConfig))]
public class IntegratedOutboundWriteLoopBenchmarks
{
    public enum PayloadKind
    {
        StringShort,
        StringMedium,
        StringLong,
        BytesSmall,
        BytesLarge
    }

    [Params(
        PayloadKind.StringShort,
        PayloadKind.StringMedium,
        PayloadKind.StringLong,
        PayloadKind.BytesSmall,
        PayloadKind.BytesLarge)]
    public PayloadKind Payload { get; set; }

    private const string BenchmarkConfig = @"
akka.actor.provider = remote
akka.log-dead-letters = off
akka.remote.dot-netty.tcp.port = 0
akka.remote.dot-netty.tcp.hostname = 127.0.0.1
akka.remote.dot-netty.tcp.public-hostname = 127.0.0.1
akka.actor.serialization-settings.allow-unregistered-types = on";

    private ActorSystem _system = null!;
    private ExtendedActorSystem _extendedSystem = null!;
    private AkkaPduProtobuffCodec _codec = null!;
    private Information _transportInfo = null!;
    private RemoteActorRef _recipient = null!;
    private IActorRef _sender = null!;
    private EndpointManager.Send _send = null!;
    private BenchmarkSend _benchmarkSend;
    private byte[] _recipientActorRefBytes = null!;
    private byte[] _senderActorRefBytes = null!;
    private PooledFrameWriter _writer = null!;
    private Serializer _serializer = null!;
    private string? _manifest;

    [GlobalSetup]
    public void Setup()
    {
        _system = ActorSystem.Create("integrated-outbound-bench", ConfigurationFactory.ParseString(BenchmarkConfig));
        _extendedSystem = (ExtendedActorSystem)_system;
        _codec = new AkkaPduProtobuffCodec(_system);
        _transportInfo = _extendedSystem.Provider.SerializationInformation;

        var root = new RootActorPath(_extendedSystem.Provider.DefaultAddress) / "user" / "bench-recipient";
        _recipient = new RemoteActorRef(RARP.For(_extendedSystem).Provider.Transport, _extendedSystem.Provider.DefaultAddress, root,
            ActorRefs.Nobody, Props.None, Deploy.None);
        _sender = _system.DeadLetters;

        var payload = CreatePayload(Payload);
        _send = new EndpointManager.Send(payload, _recipient, _sender, new SeqNo(1));
        _benchmarkSend = new BenchmarkSend(payload, _recipient, _sender, new SeqNo(1));

        _recipientActorRefBytes = new ActorRefData { Path = _recipient.Path.ToSerializationFormat() }.ToByteArray();
        _senderActorRefBytes = new ActorRefData { Path = _sender.Path.ToSerializationFormatWithAddress(_extendedSystem.Provider.DefaultAddress) }.ToByteArray();
        _writer = new PooledFrameWriter(1024);

        _serializer = _extendedSystem.Serialization.FindSerializerFor(payload);
        _manifest = _serializer switch
        {
            SerializerWithStringManifest withStringManifest => withStringManifest.Manifest(payload),
            _ when _serializer.IncludeManifest => payload.GetType().TypeQualifiedName(),
            _ => null
        };

        VerifyWireCompatibility();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _writer.Dispose();
        _system.Terminate().Wait(TimeSpan.FromSeconds(5));
    }

    [IterationSetup]
    public void ResetWriter()
    {
        _writer.Reset();
    }

    [Benchmark(Baseline = true, Description = "Current: split MessageSerializer + AkkaPduCodec + transport frame")]
    public int CurrentSplitOutboundPath()
    {
        var current = BuildCurrentFrame();
        return current.Length;
    }

    [Benchmark(Description = "Spike: integrated outbound writer loop on send-shaped work")]
    public int IntegratedOutboundWriterLoop()
    {
        return BuildIntegratedFrame();
    }

    private static object CreatePayload(PayloadKind kind)
    {
        return kind switch
        {
            PayloadKind.StringShort => "hello",
            PayloadKind.StringMedium => new string('m', 256),
            PayloadKind.StringLong => new string('l', 4096),
            PayloadKind.BytesSmall => CreateBytes(16),
            PayloadKind.BytesLarge => CreateBytes(16 * 1024),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static byte[] CreateBytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)i;
        return bytes;
    }

    private byte[] BuildCurrentFrame()
    {
        var serialized = MessageSerializer.Serialize(_extendedSystem, _transportInfo, _send.Message);
        var message = _codec.ConstructMessage(_send.Recipient.LocalAddressToUse, _send.Recipient, serialized,
            _send.SenderOption, _send.Seq, ackOption: null);
        var protocolPayload = _codec.ConstructPayload(message).ToByteArray();

        var frame = new byte[sizeof(int) + protocolPayload.Length];
        BitConverter.TryWriteBytes(frame.AsSpan(0, sizeof(int)), protocolPayload.Length);
        protocolPayload.CopyTo(frame.AsSpan(sizeof(int)));
        return frame;
    }

    private int BuildIntegratedFrame()
    {
        _writer.Reset();
        _writer.Advance(sizeof(int));

        var protocolStart = _writer.WrittenCount;
        IntegratedProtobufWire.WriteTag(_writer, 1, IntegratedProtobufWire.WireTypeLengthDelimited);
        var protocolPayloadLengthOffset = _writer.ReserveFixedWidthLength();
        var protocolPayloadStart = _writer.WrittenCount;

        IntegratedProtobufWire.WriteTag(_writer, 2, IntegratedProtobufWire.WireTypeLengthDelimited);
        var envelopeLengthOffset = _writer.ReserveFixedWidthLength();
        var envelopeStart = _writer.WrittenCount;

        IntegratedProtobufWire.WriteTag(_writer, 1, IntegratedProtobufWire.WireTypeLengthDelimited);
        IntegratedProtobufWire.WriteLengthPrefixedBytes(_writer, _recipientActorRefBytes);

        IntegratedProtobufWire.WriteTag(_writer, 2, IntegratedProtobufWire.WireTypeLengthDelimited);
        var payloadLengthOffset = _writer.ReserveFixedWidthLength();
        var payloadStart = _writer.WrittenCount;

        IntegratedProtobufWire.WriteTag(_writer, 1, IntegratedProtobufWire.WireTypeLengthDelimited);
        var messageLengthOffset = _writer.ReserveFixedWidthLength();
        var payloadLength = WritePayloadDirect(_benchmarkSend.Message);
        _writer.PatchFixedWidthLength(messageLengthOffset, payloadLength);

        IntegratedProtobufWire.WriteTag(_writer, 2, IntegratedProtobufWire.WireTypeVarint);
        IntegratedProtobufWire.WriteVarint32(_writer, (uint)_serializer.Identifier);

        if (!string.IsNullOrEmpty(_manifest))
        {
            IntegratedProtobufWire.WriteTag(_writer, 3, IntegratedProtobufWire.WireTypeLengthDelimited);
            IntegratedProtobufWire.WriteString(_writer, _manifest);
        }

        _writer.PatchFixedWidthLength(payloadLengthOffset, _writer.WrittenCount - payloadStart);

        IntegratedProtobufWire.WriteTag(_writer, 4, IntegratedProtobufWire.WireTypeLengthDelimited);
        IntegratedProtobufWire.WriteLengthPrefixedBytes(_writer, _senderActorRefBytes);

        IntegratedProtobufWire.WriteTag(_writer, 5, IntegratedProtobufWire.WireTypeFixed64);
        IntegratedProtobufWire.WriteFixed64(_writer, (ulong)_benchmarkSend.Seq.RawValue);

        _writer.PatchFixedWidthLength(envelopeLengthOffset, _writer.WrittenCount - envelopeStart);
        _writer.PatchFixedWidthLength(protocolPayloadLengthOffset, _writer.WrittenCount - protocolPayloadStart);

        _writer.PatchFrameLengthLittleEndian(_writer.WrittenCount - protocolStart);
        return _writer.WrittenCount;
    }

    private int WritePayloadDirect(object payload)
    {
        switch (payload)
        {
            case string value:
            {
                var byteCount = Encoding.UTF8.GetByteCount(value);
                var span = _writer.GetSpan(byteCount);
                var written = Encoding.UTF8.GetBytes(value.AsSpan(), span);
                _writer.Advance(written);
                return written;
            }
            case byte[] bytes:
                _writer.Write(bytes);
                return bytes.Length;
            default:
            {
                var serialized = _serializer.ToBinary(payload);
                _writer.Write(serialized);
                return serialized.Length;
            }
        }
    }

    private void VerifyWireCompatibility()
    {
        var current = BuildCurrentFrame();
        BuildIntegratedFrame();

        var currentProtocol = AkkaProtocolMessage.Parser.ParseFrom(current.AsSpan(sizeof(int)).ToArray());
        var integratedProtocol = AkkaProtocolMessage.Parser.ParseFrom(_writer.WrittenSpan.Slice(sizeof(int)).ToArray());

        var currentEnvelope = AckAndEnvelopeContainer.Parser.ParseFrom(currentProtocol.Payload);
        var integratedEnvelope = AckAndEnvelopeContainer.Parser.ParseFrom(integratedProtocol.Payload);

        if (currentEnvelope.Envelope.Recipient.Path != integratedEnvelope.Envelope.Recipient.Path)
            throw new InvalidOperationException("Integrated write loop changed the recipient path.");

        if (currentEnvelope.Envelope.Sender.Path != integratedEnvelope.Envelope.Sender.Path)
            throw new InvalidOperationException("Integrated write loop changed the sender path.");

        if (currentEnvelope.Envelope.Seq != integratedEnvelope.Envelope.Seq)
            throw new InvalidOperationException("Integrated write loop changed the sequence number.");

        if (currentEnvelope.Envelope.Message.SerializerId != integratedEnvelope.Envelope.Message.SerializerId)
            throw new InvalidOperationException("Integrated write loop changed the serializer id.");

        if (!currentEnvelope.Envelope.Message.Message.Equals(integratedEnvelope.Envelope.Message.Message))
            throw new InvalidOperationException("Integrated write loop changed the serialized payload bytes.");

        if (!currentEnvelope.Envelope.Message.MessageManifest.Equals(integratedEnvelope.Envelope.Message.MessageManifest))
            throw new InvalidOperationException("Integrated write loop changed the payload manifest.");
    }

    private readonly record struct BenchmarkSend(object Message, RemoteActorRef Recipient, IActorRef SenderOption, SeqNo Seq);

    private sealed class PooledFrameWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] _buffer;
        private int _written;
        private bool _disposed;

        public PooledFrameWriter(int initialCapacity)
        {
            _buffer = ArrayPool<byte>.Shared.Rent(initialCapacity);
        }

        public int WrittenCount => _written;
        public ReadOnlySpan<byte> WrittenSpan => _buffer.AsSpan(0, _written);

        public void Reset()
        {
            _written = 0;
        }

        public void Advance(int count)
        {
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

        public int ReserveFixedWidthLength()
        {
            var offset = _written;
            var span = GetSpan(IntegratedProtobufWire.FixedWidthVarintBytes);
            span[0] = 0x80;
            span[1] = 0x80;
            span[2] = 0x80;
            span[3] = 0x80;
            span[4] = 0x00;
            Advance(IntegratedProtobufWire.FixedWidthVarintBytes);
            return offset;
        }

        public void PatchFixedWidthLength(int offset, int length)
        {
            IntegratedProtobufWire.PatchFixedWidthVarint(_buffer.AsSpan(offset, IntegratedProtobufWire.FixedWidthVarintBytes),
                (uint)length);
        }

        public void PatchFrameLengthLittleEndian(int payloadLength)
        {
            BitConverter.TryWriteBytes(_buffer.AsSpan(0, sizeof(int)), payloadLength);
        }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            var span = GetSpan(bytes.Length);
            bytes.CopyTo(span);
            Advance(bytes.Length);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = Array.Empty<byte>();
            _disposed = true;
        }

        private void EnsureCapacity(int sizeHint)
        {
            if (sizeHint <= 0)
                sizeHint = 1;

            var required = _written + sizeHint;
            if (required <= _buffer.Length)
                return;

            var newSize = _buffer.Length;
            while (newSize < required)
                newSize *= 2;

            var newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
            Buffer.BlockCopy(_buffer, 0, newBuffer, 0, _written);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = newBuffer;
        }
    }

    private static class IntegratedProtobufWire
    {
        public const int FixedWidthVarintBytes = 5;
        public const byte WireTypeVarint = 0;
        public const byte WireTypeFixed64 = 1;
        public const byte WireTypeLengthDelimited = 2;

        public static void WriteTag(IBufferWriter<byte> writer, int fieldNumber, byte wireType)
        {
            WriteVarint32(writer, (uint)((fieldNumber << 3) | wireType));
        }

        public static void WriteVarint32(IBufferWriter<byte> writer, uint value)
        {
            var span = writer.GetSpan(FixedWidthVarintBytes);
            var written = 0;

            while (value >= 0x80)
            {
                span[written++] = (byte)(value | 0x80);
                value >>= 7;
            }

            span[written++] = (byte)value;
            writer.Advance(written);
        }

        public static void PatchFixedWidthVarint(Span<byte> span, uint value)
        {
            span[0] = (byte)((value & 0x7F) | 0x80);
            span[1] = (byte)(((value >> 7) & 0x7F) | 0x80);
            span[2] = (byte)(((value >> 14) & 0x7F) | 0x80);
            span[3] = (byte)(((value >> 21) & 0x7F) | 0x80);
            span[4] = (byte)((value >> 28) & 0x7F);
        }

        public static void WriteFixed64(IBufferWriter<byte> writer, ulong value)
        {
            var span = writer.GetSpan(sizeof(ulong));
            BitConverter.TryWriteBytes(span, value);
            writer.Advance(sizeof(ulong));
        }

        public static void WriteString(IBufferWriter<byte> writer, string value)
        {
            var byteCount = Encoding.UTF8.GetByteCount(value);
            WriteVarint32(writer, (uint)byteCount);
            var span = writer.GetSpan(byteCount);
            var written = Encoding.UTF8.GetBytes(value.AsSpan(), span);
            writer.Advance(written);
        }

        public static void WriteLengthPrefixedBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> bytes)
        {
            WriteVarint32(writer, (uint)bytes.Length);
            var span = writer.GetSpan(bytes.Length);
            bytes.CopyTo(span);
            writer.Advance(bytes.Length);
        }
    }
}
