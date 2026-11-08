//-----------------------------------------------------------------------
// <copyright file="ReplicatorProtocolSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using Akka.DistributedData.Internal;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Remote.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Compare actual id 12/52 bindings across byte arrays, native buffer writes and reads.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ReplicatorProtocolSerializerBenchmarks
{
    private ExtendedActorSystem _legacySystem = null!;
    private ExtendedActorSystem _v2System = null!;
    private Serializer _legacy = null!;
    private SerializerV2 _legacyAdapter = null!;
    private SerializerV2 _generated = null!;
    private object _message = null!;
    private string _manifest = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _v2Bytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("get-local", "write-ack", "data-envelope-metadata", "gossip")]
    public string Shape { get; set; } = "get-local";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(true));
        // Reviewed pre-port protobuf records construct the private protocol shapes via the
        // unchanged public reader. The module reader supplies the production domain instance.
        var (manifest, hex) = Shape switch
        {
            "get-local" => ("A", "0a120a0b0a07636f756e7465721002100b2201661002"),
            "write-ack" => ("J", ""),
            "data-envelope-metadata" => ("H", "0a5e0a570a2c0a200a130a0e7365636f6e642e6578616d706c6510f8131087808080808080808001120802000000000000000a270a1b0a120a0d66697273742e6578616d706c6510f8131080808080802012081100000000000000100b220146124e0a160a120a0d74686972642e6578616d706c6510f813100312200a130a0e7365636f6e642e6578616d706c6510f813108780808080808080800122120a0d66697273742e6578616d706c6510f81312230a1b0a120a0d66697273742e6578616d706c6510f8131080808080802018012880890f1a470a240a200a130a0e7365636f6e642e6578616d706c6510f813108780808080808080800110020a1f0a1b0a120a0d66697273742e6578616d706c6510f813108080808080201004"),
            "gossip" => ("N", "1f8b0800000000000003e360144ae5624c144ae08ae30ae7d2e152e012e6e22b4e4dcecf4bd14bad48cc2dc84915f8212cd0de00058c421c4c0c10c0a5ce25cd25c4c59b9659545c82ac18ac5041884310aa50805b89d14d680913176392d03c269a5be4c72506525d929159846234b310413b95b0d822a48cd77609468d864e7e29772e15c25e1260e292c76b98008b24cc2f8ac2508606a30123007e120eaea6010000"),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _message = _legacySystem.Serialization.Deserialize(Convert.FromHexString(hex), 12, manifest);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 12 || _generated.Identifier != 52 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Protocol benchmark selected the wrong ids or manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        // Gossip's unordered map has process-random hash enumeration. Its complete logical
        // fields are checked below; compressed entry order is not a canonical wire contract.
        if (_manifest != "N" && !_legacyBytes.AsSpan().SequenceEqual(Convert.FromHexString(hex)))
            throw new InvalidOperationException("Frozen bootstrap changed protocol fields or actor-reference paths.");
        _v2Bytes = _generated.ToBinary(_message);
        var reader = _legacy;
        foreach (var restored in new[]
                 {
                     _legacySystem.Serialization.Deserialize(_v2Bytes, 52, _manifest),
                     _generated.FromBinary(_v2Bytes, _manifest),
                     _legacy.FromBinary(_legacyBytes, _manifest),
                     _legacySystem.Serialization.Deserialize(_legacyBytes, 12, _manifest)
                 })
        {
            if (restored.GetType() != _message.GetType())
                throw new InvalidOperationException("Protocol benchmark changed the runtime type.");
            if (_manifest == "N")
                ValidateGossip(_message, restored);
            else if (!reader.ToBinary(restored).AsSpan().SequenceEqual(reader.ToBinary(_message)))
                throw new InvalidOperationException("Protocol benchmark failed full-member validation.");
        }
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    // Gossip is internal; this bounded JIT-only setup inspects its four public protocol
    // properties. DataEnvelope.Equals checks data, every pruning entry and delta versions.
    // No reflective operation is timed and no benchmark-only production API is introduced.
    private static void ValidateGossip(object expected, object actual)
    {
        var type = expected.GetType();
        foreach (var name in new[] { "SendBack", "ToSystemUid", "FromSystemUid" })
        {
            var property = type.GetProperty(name) ?? throw new InvalidOperationException("Missing gossip field " + name);
            if (!Equals(property.GetValue(expected), property.GetValue(actual)))
                throw new InvalidOperationException("Gossip field changed: " + name);
        }
        var updated = type.GetProperty("UpdatedData") ?? throw new InvalidOperationException("Missing gossip map.");
        var left = (IImmutableDictionary<string, DataEnvelope>)updated.GetValue(expected)!;
        var right = (IImmutableDictionary<string, DataEnvelope>)updated.GetValue(actual)!;
        if (left.Count != 2 || left.Count != right.Count || !left.ContainsKey("a") || !left.ContainsKey("b"))
            throw new InvalidOperationException("Gossip fixture keys changed.");
        foreach (var entry in left)
        {
            if (!right.TryGetValue(entry.Key, out var value) || !entry.Value.Equals(value))
                throw new InvalidOperationException("Gossip data/pruning/delta state changed: " + entry.Key);
        }
    }

    private static Config Config(bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = local
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        """).WithFallback(Akka.DistributedData.DistributedData.DefaultConfig()).WithFallback(ConfigurationFactory.FromResource<PrimitiveSerializers>("Akka.Remote.Configuration.Remote.conf"));

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await Task.WhenAll(_legacySystem.Terminate(), _v2System.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Benchmark]
    public byte[] ProtobufSerialize() => _legacy.ToBinary(_message);

    [Benchmark]
    public byte[] MessagePackSerialize() => _generated.ToBinary(_message);

    [Benchmark]
    public int ProtobufBufferWrite() => WriteToBuffer(_legacyAdapter);

    [Benchmark]
    public int MessagePackBufferWrite() => WriteToBuffer(_generated);

    [Benchmark]
    public object ProtobufDeserialize() => _legacy.FromBinary(_legacyBytes, _manifest);

    [Benchmark]
    public object MessagePackDeserialize() => _generated.FromBinary(_v2Bytes, _manifest);

    private int WriteToBuffer(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }
}
