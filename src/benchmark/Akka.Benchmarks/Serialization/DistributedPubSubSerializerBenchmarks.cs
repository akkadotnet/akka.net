// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Registry status/delta plus independently serialized command payload envelopes.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class DistributedPubSubSerializerBenchmarks
{
    private ExtendedActorSystem _legacySystem = null!;
    private ExtendedActorSystem _generatedSystem = null!;
    private Serializer _legacy = null!;
    private SerializerV2 _adapter = null!;
    private SerializerV2 _generated = null!;
    private object _message = null!;
    private string _manifest = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _generatedBytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("StatusReply", "DeltaTombstone", "Send", "Publish")]
    public string Shape { get; set; } = "StatusReply";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("pubsub-bench-legacy", Config(false));
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("pubsub-bench-generated", Config(true));
        _message = Shape switch
        {
            // Frozen pre-port registry records expose the private domain values through id 9.
            "StatusReply" => _legacySystem.Serialization.Deserialize(Convert.FromHexString(
                "0a240a200a0670756273756212093132372e302e302e3118f8132208616b6b612e74637010111001"), 9, "A"),
            "DeltaTombstone" => _legacySystem.Serialization.Deserialize(Convert.FromHexString(
                "0a360a200a0670756273756212093132372e302e302e3118f8132208616b6b612e74637010031a100a0a2f757365722f676f6e6512020807"), 9, "B"),
            "Send" => new Send("/user/worker", new string('x', 128), true),
            "Publish" => new Publish("topic", new string('x', 128), true),
            _ => throw new InvalidOperationException($"Unknown shape [{Shape}].")
        };
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_generatedSystem.Serialization.FindSerializerFor(_message);
        _adapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = _legacy.Manifest(_message);
        if (_legacy.Identifier != 9 || _generated.Identifier != 49 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("PubSub benchmark selected incorrect writers/manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _generatedBytes = _generated.ToBinary(_message);
        Validate(_legacySystem.Serialization.Deserialize(_generatedBytes, 49, _manifest));
        Validate(_generatedSystem.Serialization.Deserialize(_legacyBytes, 9, _manifest));
        Validate(_legacy.FromBinary(_legacyBytes, _manifest));
        Validate(_generated.FromBinary(_generatedBytes, _manifest));
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_generatedBytes.Length}B");
    }

    private void Validate(object value)
    {
        // Domain Equals omits some registry fields; compare every legacy wire field instead.
        if (value.GetType() != _message.GetType() || !_legacy.ToBinary(value).AsSpan().SequenceEqual(_legacyBytes))
            throw new InvalidOperationException("PubSub benchmark lost a registry/payload field.");
    }

    private static Config Config(bool enabled) => ConfigurationFactory.ParseString(
        $"akka.actor.provider = local\nakka.actor.serialization-v2 = {(enabled ? "on" : "off")}");

    [GlobalCleanup]
    public async Task Cleanup() => await Task.WhenAll(_legacySystem.Terminate(), _generatedSystem.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));

    [Benchmark] public byte[] ProtobufSerialize() => _legacy.ToBinary(_message);
    [Benchmark] public byte[] MessagePackSerialize() => _generated.ToBinary(_message);
    [Benchmark] public int ProtobufBufferWrite() => Write(_adapter);
    [Benchmark] public int MessagePackBufferWrite() => Write(_generated);
    [Benchmark] public object ProtobufDeserialize() => _legacy.FromBinary(_legacyBytes, _manifest);
    [Benchmark] public object MessagePackDeserialize() => _generated.FromBinary(_generatedBytes, _manifest);

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }
}
