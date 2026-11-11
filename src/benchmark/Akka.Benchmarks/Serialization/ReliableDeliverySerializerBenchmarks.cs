//-----------------------------------------------------------------------
// <copyright file="ReliableDeliverySerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// Protobuf-vs-MessagePack A/B for the ReliableDelivery (Akka.Delivery) internal serializers:
/// legacy <see cref="ReliableDeliverySerializer"/> (id 36, protobuf) vs the forked
/// <see cref="ReliableDeliveryMessagePackSerializer"/> (id 76, source-generated
/// Akka.Serialization.V2 MessagePack). This pairing is the acceptance gate for the
/// protobuf-to-MessagePack internal-serializer migration - CPU, allocations, AND serialized
/// payload size (printed from <see cref="Setup"/>) are all first-class metrics.
///
/// Message shapes:
/// <list type="bullet">
/// <item><c>Request</c> - small flow-control message (hot path, sent per demand signal);</item>
/// <item><c>SequencedMessage</c> - the envelope wrapping EVERY delivered user message (hottest
/// path), carrying a 128-char string payload through the opaque user-payload envelope;</item>
/// <item><c>SequencedMessageChunked</c> - a 1 KiB chunk of a large message riding through as
/// raw bytes;</item>
/// <item><c>State</c> - durable-queue snapshot (cold path) with confirmed map + 10 unconfirmed
/// messages, stressing collection/object-graph encoding.</item>
/// </list>
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ReliableDeliverySerializerBenchmarks
{
    private ExtendedActorSystem _system = null!;
    private ReliableDeliverySerializer _protobuf = null!;
    private ReliableDeliveryMessagePackSerializer _msgPack = null!;
    private SerializerV1Adapter _protobufAdapter = null!;
    private readonly ArrayBufferWriter<byte> _bufferWriter = new();

    private ProducerController.Request _request = null!;
    private ConsumerController.SequencedMessage<string> _sequenced = null!;
    private ConsumerController.SequencedMessage<string> _sequencedChunked = null!;
    private DurableProducerQueue.State<string> _state;

    private byte[] _requestProtobufBytes = null!;
    private byte[] _requestMsgPackBytes = null!;
    private byte[] _sequencedProtobufBytes = null!;
    private byte[] _sequencedMsgPackBytes = null!;
    private byte[] _sequencedChunkedProtobufBytes = null!;
    private byte[] _sequencedChunkedMsgPackBytes = null!;
    private byte[] _stateProtobufBytes = null!;
    private byte[] _stateMsgPackBytes = null!;

    private string _requestManifest = null!;
    private string _sequencedManifest = null!;
    private string _stateManifest = null!;

    [GlobalSetup]
    public void Setup()
    {
        // ClusterSerializers registers BOTH serializers (id 36 protobuf-bound, id 76 additive);
        // the arms below drive the serializer instances directly for a clean A/B.
        _system = (ExtendedActorSystem)ActorSystem.Create("rd-serializer-bench", ClusterConfigFactory.Default());
        _protobuf = new ReliableDeliverySerializer(_system);
        _msgPack = new ReliableDeliveryMessagePackSerializer(_system);
        _protobufAdapter = new SerializerV1Adapter(_system, _protobuf);

        var producerController = _system.ActorOf(Props.Empty, "producer-controller");

        _request = new ProducerController.Request(100L, 150L, true, false);
        _sequenced = new ConsumerController.SequencedMessage<string>("producer-1", 17L,
            new string('x', 128), false, true, producerController);
        var chunkBytes = new byte[1024];
        new Random(42).NextBytes(chunkBytes);
        _sequencedChunked = ConsumerController.SequencedMessage<string>.FromChunkedMessage("producer-1", 18L,
            new ChunkedMessage(chunkBytes, true, false, 17, "S"), false, true, producerController);

        var confirmed = ImmutableDictionary<string, (long, long)>.Empty
            .Add("q1", (5L, 1000L)).Add("q2", (7L, 2000L)).Add("q3", (9L, 3000L)).Add("q4", (11L, 4000L));
        var unconfirmed = ImmutableList.CreateBuilder<DurableProducerQueue.MessageSent<string>>();
        for (var i = 0; i < 10; i++)
            unconfirmed.Add(new DurableProducerQueue.MessageSent<string>(12L + i, $"message-{i:D3}-payload", true,
                "q4", 5000L + i));
        _state = new DurableProducerQueue.State<string>(22L, 11L, confirmed, unconfirmed.ToImmutable());

        _requestManifest = _protobuf.Manifest(_request);
        _sequencedManifest = _protobuf.Manifest(_sequenced);
        _stateManifest = _protobuf.Manifest(_state);
        AssertManifestParity();

        _requestProtobufBytes = Request_protobuf_serialize();
        _requestMsgPackBytes = Request_msgpack_serialize();
        _sequencedProtobufBytes = SequencedMessage_protobuf_serialize();
        _sequencedMsgPackBytes = SequencedMessage_msgpack_serialize();
        _sequencedChunkedProtobufBytes = SequencedMessageChunked_protobuf_serialize();
        _sequencedChunkedMsgPackBytes = SequencedMessageChunked_msgpack_serialize();
        _stateProtobufBytes = State_protobuf_serialize();
        _stateMsgPackBytes = State_msgpack_serialize();

        Require(_protobuf.Identifier == 36 && _msgPack.Identifier == 76
            && _system.Serialization.GetSerializerById(36) is ReliableDeliverySerializer
            && _system.Serialization.GetSerializerById(76) is ReliableDeliveryMessagePackSerializer, "registered serializer ids");
        ValidateMessage(_request, Request_protobuf_deserialize());
        ValidateMessage(_request, Request_msgpack_deserialize());
        ValidateMessage(_sequenced, SequencedMessage_protobuf_deserialize());
        ValidateMessage(_sequenced, SequencedMessage_msgpack_deserialize());
        ValidateMessage(_sequencedChunked, SequencedMessageChunked_protobuf_deserialize());
        ValidateMessage(_sequencedChunked, SequencedMessageChunked_msgpack_deserialize());
        ValidateMessage(_state, State_protobuf_deserialize());
        ValidateMessage(_state, State_msgpack_deserialize());
        ValidateWriter(Request_protobuf_buffer_writer(), _requestProtobufBytes);
        ValidateWriter(Request_msgpack_buffer_writer(), _requestMsgPackBytes);
        ValidateWriter(SequencedMessage_protobuf_buffer_writer(), _sequencedProtobufBytes);
        ValidateWriter(SequencedMessage_msgpack_buffer_writer(), _sequencedMsgPackBytes);
        ValidateWriter(SequencedMessageChunked_protobuf_buffer_writer(), _sequencedChunkedProtobufBytes);
        ValidateWriter(SequencedMessageChunked_msgpack_buffer_writer(), _sequencedChunkedMsgPackBytes);
        ValidateWriter(State_protobuf_buffer_writer(), _stateProtobufBytes);
        ValidateWriter(State_msgpack_buffer_writer(), _stateMsgPackBytes);
        foreach (var (message, protobuf, msgPack, manifest) in new[]
                 {
                     ((object)_request, _requestProtobufBytes, _requestMsgPackBytes, _requestManifest),
                     ((object)_sequenced, _sequencedProtobufBytes, _sequencedMsgPackBytes, _sequencedManifest),
                     ((object)_sequencedChunked, _sequencedChunkedProtobufBytes, _sequencedChunkedMsgPackBytes, _sequencedManifest),
                     ((object)_state, _stateProtobufBytes, _stateMsgPackBytes, _stateManifest)
                 })
        {
            ValidateMessage(message, _system.Serialization.Deserialize(protobuf, 36, manifest));
            ValidateMessage(message, _system.Serialization.Deserialize(msgPack, 76, manifest));
        }

        // serialized payload size is a first-class gate metric - report it side by side
        Console.WriteLine("ReliableDelivery payload sizes (bytes): message | protobuf (id 36) | MessagePack (id 76)");
        PrintSize("Request", _requestProtobufBytes, _requestMsgPackBytes);
        PrintSize("SequencedMessage (128-char payload)", _sequencedProtobufBytes, _sequencedMsgPackBytes);
        PrintSize("SequencedMessage (1 KiB chunk)", _sequencedChunkedProtobufBytes, _sequencedChunkedMsgPackBytes);
        PrintSize("State (4 confirmed / 10 unconfirmed)", _stateProtobufBytes, _stateMsgPackBytes);
    }

    private void AssertManifestParity()
    {
        if (_msgPack.Manifest(_request) != _requestManifest ||
            _msgPack.Manifest(_sequenced) != _sequencedManifest ||
            _msgPack.Manifest(_sequencedChunked) != _sequencedManifest ||
            _msgPack.Manifest(_state) != _stateManifest)
            throw new InvalidOperationException("Manifest parity violated between protobuf and MessagePack forks.");
    }

    private static void PrintSize(string label, byte[] protobuf, byte[] msgPack)
    {
        var delta = (msgPack.Length - protobuf.Length) * 100.0 / protobuf.Length;
        Console.WriteLine($"{label}: {protobuf.Length} | {msgPack.Length} ({delta:+0.0;-0.0}%)");
    }

    private void ValidateWriter(int count, byte[] expected)
    {
        Require(count == _bufferWriter.WrittenCount, "reported writer count");
        Require(_bufferWriter.WrittenSpan.SequenceEqual(expected), "array/buffer byte parity");
    }

    private static void ValidateMessage(object expected, object actual)
    {
        Require(actual.GetType() == expected.GetType(), "root runtime type");
        switch (expected)
        {
            case ProducerController.Request expectedRequest:
                var request = (ProducerController.Request)actual;
                Require(request.ConfirmedSeqNr == expectedRequest.ConfirmedSeqNr
                    && request.RequestUpToSeqNr == expectedRequest.RequestUpToSeqNr
                    && request.SupportResend == expectedRequest.SupportResend
                    && request.ViaTimeout == expectedRequest.ViaTimeout, "Request fields");
                break;
            case ConsumerController.SequencedMessage<string> expectedMessage:
                var message = (ConsumerController.SequencedMessage<string>)actual;
                Require(message.ProducerId == expectedMessage.ProducerId && message.SeqNr == expectedMessage.SeqNr
                    && message.First == expectedMessage.First && message.Ack == expectedMessage.Ack
                    && message.ProducerController.Equals(expectedMessage.ProducerController)
                    && message.Message.IsMessage == expectedMessage.Message.IsMessage, "SequencedMessage fields");
                if (expectedMessage.Message.Chunk is { } expectedChunk)
                {
                    Require(message.Message.Chunk is { } chunk && chunk.FirstChunk == expectedChunk.FirstChunk
                        && chunk.LastChunk == expectedChunk.LastChunk && chunk.SerializerId == expectedChunk.SerializerId
                        && chunk.Manifest == expectedChunk.Manifest
                        && chunk.SerializedMessage.Span.SequenceEqual(expectedChunk.SerializedMessage.Span), "chunk fields and bytes");
                }
                else
                {
                    Require(message.Message.Chunk is null && message.Message.Message == expectedMessage.Message.Message,
                        "complete user payload");
                }
                break;
            case DurableProducerQueue.State<string> expectedState:
                var state = (DurableProducerQueue.State<string>)actual;
                Require(state.CurrentSeqNr == expectedState.CurrentSeqNr
                    && state.HighestConfirmedSeqNr == expectedState.HighestConfirmedSeqNr
                    && state.ConfirmedSeqNr.Count == expectedState.ConfirmedSeqNr.Count
                    && state.Unconfirmed.Count == expectedState.Unconfirmed.Count, "durable state fields");
                foreach (var confirmed in expectedState.ConfirmedSeqNr)
                    Require(state.ConfirmedSeqNr.TryGetValue(confirmed.Key, out var value) && value == confirmed.Value,
                        "confirmed qualifier/sequence/timestamp");
                for (var i = 0; i < expectedState.Unconfirmed.Count; i++)
                {
                    var sent = state.Unconfirmed[i];
                    var expectedSent = expectedState.Unconfirmed[i];
                    Require(sent.SeqNr == expectedSent.SeqNr && sent.Ack == expectedSent.Ack
                        && sent.ConfirmationQualifier == expectedSent.ConfirmationQualifier
                        && sent.Timestamp == expectedSent.Timestamp && sent.Message.IsMessage
                        && sent.Message.Chunk is null && sent.Message.Message == expectedSent.Message.Message,
                        "ordered unconfirmed message fields");
                }
                break;
            default:
                throw new InvalidOperationException($"No Delivery benchmark validator for [{expected.GetType()}].");
        }
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"Reliable Delivery benchmark setup failed: {contract}.");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await _system.Terminate();
    }

    // ----- Request (small flow-control message) -----

    [Benchmark]
    public byte[] Request_protobuf_serialize() => _protobuf.ToBinary(_request);

    [Benchmark]
    public byte[] Request_msgpack_serialize() => _msgPack.ToBinary(_request);

    [Benchmark]
    public int Request_protobuf_buffer_writer() => SerializeToBuffer(_protobufAdapter, _request);

    [Benchmark]
    public int Request_msgpack_buffer_writer() => SerializeToBuffer(_msgPack, _request);

    [Benchmark]
    public object Request_protobuf_deserialize() => _protobuf.FromBinary(_requestProtobufBytes, _requestManifest);

    [Benchmark]
    public object Request_msgpack_deserialize() => _msgPack.FromBinary(_requestMsgPackBytes, _requestManifest);

    // ----- SequencedMessage (user payload through the opaque envelope; hottest path) -----

    [Benchmark]
    public byte[] SequencedMessage_protobuf_serialize() => _protobuf.ToBinary(_sequenced);

    [Benchmark]
    public byte[] SequencedMessage_msgpack_serialize() => _msgPack.ToBinary(_sequenced);

    [Benchmark]
    public int SequencedMessage_protobuf_buffer_writer() => SerializeToBuffer(_protobufAdapter, _sequenced);

    [Benchmark]
    public int SequencedMessage_msgpack_buffer_writer() => SerializeToBuffer(_msgPack, _sequenced);

    [Benchmark]
    public object SequencedMessage_protobuf_deserialize() =>
        _protobuf.FromBinary(_sequencedProtobufBytes, _sequencedManifest);

    [Benchmark]
    public object SequencedMessage_msgpack_deserialize() =>
        _msgPack.FromBinary(_sequencedMsgPackBytes, _sequencedManifest);

    // ----- SequencedMessage carrying a 1 KiB chunk (large-message chunking path) -----

    [Benchmark]
    public byte[] SequencedMessageChunked_protobuf_serialize() => _protobuf.ToBinary(_sequencedChunked);

    [Benchmark]
    public byte[] SequencedMessageChunked_msgpack_serialize() => _msgPack.ToBinary(_sequencedChunked);

    [Benchmark]
    public int SequencedMessageChunked_protobuf_buffer_writer() => SerializeToBuffer(_protobufAdapter, _sequencedChunked);

    [Benchmark]
    public int SequencedMessageChunked_msgpack_buffer_writer() => SerializeToBuffer(_msgPack, _sequencedChunked);

    [Benchmark]
    public object SequencedMessageChunked_protobuf_deserialize() =>
        _protobuf.FromBinary(_sequencedChunkedProtobufBytes, _sequencedManifest);

    [Benchmark]
    public object SequencedMessageChunked_msgpack_deserialize() =>
        _msgPack.FromBinary(_sequencedChunkedMsgPackBytes, _sequencedManifest);

    // ----- DurableProducerQueue.State (cold path, collection-heavy) -----

    [Benchmark]
    public byte[] State_protobuf_serialize() => _protobuf.ToBinary(_state);

    [Benchmark]
    public byte[] State_msgpack_serialize() => _msgPack.ToBinary(_state);

    [Benchmark]
    public int State_protobuf_buffer_writer() => SerializeToBuffer(_protobufAdapter, _state);

    [Benchmark]
    public int State_msgpack_buffer_writer() => SerializeToBuffer(_msgPack, _state);

    [Benchmark]
    public object State_protobuf_deserialize() => _protobuf.FromBinary(_stateProtobufBytes, _stateManifest);

    [Benchmark]
    public object State_msgpack_deserialize() => _msgPack.FromBinary(_stateMsgPackBytes, _stateManifest);

    private int SerializeToBuffer(SerializerV2 serializer, object message)
    {
        // Artery hands the serializer a writer it owns. Reuse capacity here so both arms measure
        // the serializer contract, including the legacy adapter's unavoidable byte-array copy.
        _bufferWriter.Clear();
        return serializer.Serialize(message, _bufferWriter);
    }
}
