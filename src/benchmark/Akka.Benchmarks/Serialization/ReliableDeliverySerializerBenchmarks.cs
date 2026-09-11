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
        // Cluster.conf registers BOTH serializers (id 36 protobuf-bound, id 76 additive);
        // the arms below drive the serializer instances directly for a clean A/B.
        _system = (ExtendedActorSystem)ActorSystem.Create("rd-serializer-bench", ClusterConfigFactory.Default());
        _protobuf = new ReliableDeliverySerializer(_system);
        _msgPack = new ReliableDeliveryMessagePackSerializer(_system);

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

        _requestProtobufBytes = _protobuf.ToBinary(_request);
        _requestMsgPackBytes = _msgPack.ToBinary(_request);
        _sequencedProtobufBytes = _protobuf.ToBinary(_sequenced);
        _sequencedMsgPackBytes = _msgPack.ToBinary(_sequenced);
        _sequencedChunkedProtobufBytes = _protobuf.ToBinary(_sequencedChunked);
        _sequencedChunkedMsgPackBytes = _msgPack.ToBinary(_sequencedChunked);
        _stateProtobufBytes = _protobuf.ToBinary(_state);
        _stateMsgPackBytes = _msgPack.ToBinary(_state);

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
    public object Request_protobuf_deserialize() => _protobuf.FromBinary(_requestProtobufBytes, _requestManifest);

    [Benchmark]
    public object Request_msgpack_deserialize() => _msgPack.FromBinary(_requestMsgPackBytes, _requestManifest);

    // ----- SequencedMessage (user payload through the opaque envelope; hottest path) -----

    [Benchmark]
    public byte[] SequencedMessage_protobuf_serialize() => _protobuf.ToBinary(_sequenced);

    [Benchmark]
    public byte[] SequencedMessage_msgpack_serialize() => _msgPack.ToBinary(_sequenced);

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
    public object State_protobuf_deserialize() => _protobuf.FromBinary(_stateProtobufBytes, _stateManifest);

    [Benchmark]
    public object State_msgpack_deserialize() => _msgPack.FromBinary(_stateMsgPackBytes, _stateManifest);
}
