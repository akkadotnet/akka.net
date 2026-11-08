// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Each shape is one actual fieldless handover control, not an artificial payload size.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ClusterSingletonSerializerBenchmarks
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

    [Params("HandOverToMe", "HandOverInProgress", "HandOverDone", "TakeOverFromMe")]
    public string Control { get; set; } = "HandOverToMe";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("singleton-bench-legacy", Config(false));
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("singleton-bench-generated", Config(true));
        _manifest = Control switch
        {
            "HandOverToMe" => "A", "HandOverInProgress" => "B", "HandOverDone" => "C", "TakeOverFromMe" => "D",
            _ => throw new InvalidOperationException($"Unknown control [{Control}].")
        };
        // Obtain private domain controls through their real legacy reader, without a new friend API.
        _message = _legacySystem.Serialization.Deserialize(Array.Empty<byte>(), 14, _manifest);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_generatedSystem.Serialization.FindSerializerFor(_message);
        _adapter = new SerializerV1Adapter(_legacySystem, _legacy);
        if (_legacy.Identifier != 14 || _generated.Identifier != 54 ||
            _legacy.Manifest(_message) != _manifest || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Singleton benchmark selected incorrect writers/manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _generatedBytes = _generated.ToBinary(_message);
        if (_legacyBytes.Length != 0 || !_generatedBytes.AsSpan().SequenceEqual(new byte[] { 0x80 }) ||
            !ReferenceEquals(_legacySystem.Serialization.Deserialize(_generatedBytes, 54, _manifest), _message) ||
            !ReferenceEquals(_generatedSystem.Serialization.Deserialize(_legacyBytes, 14, _manifest), _message) ||
            !ReferenceEquals(_legacy.FromBinary(_legacyBytes, _manifest), _message) ||
            !ReferenceEquals(_generated.FromBinary(_generatedBytes, _manifest), _message))
            throw new InvalidOperationException("Singleton byte/instance validation failed.");
        Console.WriteLine($"{Control}: protobuf={_legacyBytes.Length}B MessagePack={_generatedBytes.Length}B");
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
