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

[Config(typeof(MicroBenchmarkConfig))]
public class MessageContainerSerializerBenchmarks
{
    private ExtendedActorSystem _legacySystem = null!;
    private ExtendedActorSystem _generatedSystem = null!;
    private Serializer _legacy = null!;
    private SerializerV2 _adapter = null!;
    private SerializerV2 _generated = null!;
    private ActorSelectionMessage _message = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _generatedBytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("EmptyPath", "ChildNames", "PatternAndParent", "NestedSelection")]
    public string Shape { get; set; } = "EmptyPath";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("selection-bench-legacy", Config(false));
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("selection-bench-generated", Config(true));
        _message = Shape switch
        {
            "EmptyPath" => new ActorSelectionMessage("hello", []),
            "ChildNames" => new ActorSelectionMessage(new string('x', 128), [new SelectChildName("user"), new SelectChildName("worker")]),
            "PatternAndParent" => new ActorSelectionMessage(new byte[128], [new SelectChildPattern("worker-*"), new SelectParent(), new SelectChildName("leaf")]),
            "NestedSelection" => new ActorSelectionMessage(new ActorSelectionMessage("nested", [new SelectChildName("inner")]),
                [new SelectChildName("user"), new SelectChildName("outer")]),
            _ => throw new InvalidOperationException($"Unknown shape [{Shape}].")
        };
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_generatedSystem.Serialization.FindSerializerFor(_message);
        _adapter = new SerializerV1Adapter(_legacySystem, _legacy);
        if (_legacy.Identifier != 6 || _generated.Identifier != 46 || _legacy.Manifest(_message) != "" || _generated.Manifest(_message) != "a")
            throw new InvalidOperationException("Selection benchmark selected incorrect writers/manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _generatedBytes = _generated.ToBinary(_message);
        Validate(_message, (ActorSelectionMessage)_legacySystem.Serialization.Deserialize(_generatedBytes, 46, "a"));
        Validate(_message, (ActorSelectionMessage)_generatedSystem.Serialization.Deserialize(_legacyBytes, 6, ""));
        Validate(_message, (ActorSelectionMessage)_legacy.FromBinary(_legacyBytes, ""));
        Validate(_message, (ActorSelectionMessage)_generated.FromBinary(_generatedBytes, "a"));
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_generatedBytes.Length}B");
    }

    private static void Validate(ActorSelectionMessage expected, ActorSelectionMessage actual)
    {
        if (expected.WildCardFanOut != actual.WildCardFanOut || expected.Elements.Length != actual.Elements.Length)
            throw new InvalidOperationException("Selection flags/path length changed.");
        for (var index = 0; index < expected.Elements.Length; index++)
        {
            var left = expected.Elements[index];
            var right = actual.Elements[index];
            if (left.GetType() != right.GetType() ||
                (left is SelectChildName name && name.Name != ((SelectChildName)right).Name) ||
                (left is SelectChildPattern pattern && pattern.PatternStr != ((SelectChildPattern)right).PatternStr))
                throw new InvalidOperationException("Selection path element changed.");
        }
        if (expected.Message is ActorSelectionMessage nested)
            Validate(nested, (ActorSelectionMessage)actual.Message);
        else if (expected.Message is byte[] bytes)
        {
            if (actual.Message is not byte[] other || !bytes.AsSpan().SequenceEqual(other))
                throw new InvalidOperationException("Selection byte payload changed.");
        }
        else if (!Equals(expected.Message, actual.Message))
            throw new InvalidOperationException("Selection payload changed.");
    }

    private static Config Config(bool enabled) => ConfigurationFactory.ParseString(
        $"akka.actor.provider = local\nakka.actor.serialization-v2 = {(enabled ? "on" : "off")}");

    [GlobalCleanup]
    public async Task Cleanup() => await Task.WhenAll(_legacySystem.Terminate(), _generatedSystem.Terminate()).WaitAsync(TimeSpan.FromSeconds(10));

    [Benchmark] public byte[] ProtobufSerialize() => _legacy.ToBinary(_message);
    [Benchmark] public byte[] MessagePackSerialize() => _generated.ToBinary(_message);
    [Benchmark] public int ProtobufBufferWrite() => Write(_adapter);
    [Benchmark] public int MessagePackBufferWrite() => Write(_generated);
    [Benchmark] public object ProtobufDeserialize() => _legacy.FromBinary(_legacyBytes, "");
    [Benchmark] public object MessagePackDeserialize() => _generated.FromBinary(_generatedBytes, "a");

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }
}
