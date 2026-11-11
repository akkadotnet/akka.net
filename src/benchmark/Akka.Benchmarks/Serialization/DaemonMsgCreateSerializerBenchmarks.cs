//-----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Configuration;
using Akka.Remote;
using Akka.Routing;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>JIT remote-deployment envelope costs; no native actor-construction claim.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class DaemonMsgCreateSerializerBenchmarks
{
    private ExtendedActorSystem _legacySystem = null!;
    private ExtendedActorSystem _generatedSystem = null!;
    private Serializer _legacy = null!;
    private SerializerV2 _legacyAdapter = null!;
    private SerializerV2 _generated = null!;
    private DaemonMsgCreate _message = null!;
    private byte[] _legacyBytes = null!;
    private byte[] _generatedBytes = null!;
    private readonly ArrayBufferWriter<byte> _buffer = new();

    [Params("Default", "Arguments", "Deploy", "LargeArgument")]
    public string Shape { get; set; } = "Default";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("daemon-benchmark", Settings(false));
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("daemon-benchmark", Settings(true));
        var props = Props.Create<BenchmarkActor>();
        var deploy = new Deploy();
        switch (Shape)
        {
            case "Arguments":
                props = Props.Create(typeof(BenchmarkActor), "argument", 17, null);
                break;
            case "LargeArgument":
                props = Props.Create(typeof(BenchmarkActor), new string('x', 4096), 17, null);
                break;
            case "Deploy":
                deploy = new Deploy("message", ConfigurationFactory.ParseString("setting = 1"), new RoundRobinPool(3),
                    new RemoteScope(new Address("akka.tcp", "target", "127.0.0.1", 2552)), "dispatcher").WithStashCapacity(11);
                props = props.WithDeploy(new Deploy("props", Config.Empty, FromConfig.Instance,
                    Deploy.NoScopeGiven, Deploy.NoDispatcherGiven));
                break;
            case "Default":
                break;
            default:
                throw new InvalidOperationException($"Unknown shape [{Shape}].");
        }
        _message = new DaemonMsgCreate(props, deploy, "/remote/benchmark", ActorRefs.NoSender);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_generatedSystem.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _legacyBytes = _legacy.ToBinary(_message);
        _generatedBytes = _generated.ToBinary(_message);
        if (_legacy.Identifier != 3 || _legacy.Manifest(_message) != "" || _generated.Identifier != 43 || _generated.Manifest(_message) != "a")
            throw new InvalidOperationException("Incorrect daemon serializer ids/manifests.");

        ValidateMessage((DaemonMsgCreate)ProtobufDeserialize(), _legacySystem, "timed legacy reader");
        ValidateMessage((DaemonMsgCreate)MessagePackDeserialize(), _generatedSystem, "timed generated reader");
        foreach (var readerSystem in new[] { _legacySystem, _generatedSystem })
        {
            ValidateMessage((DaemonMsgCreate)readerSystem.Serialization.Deserialize(_legacyBytes, 3, ""),
                readerSystem, "registered legacy reader");
            ValidateMessage((DaemonMsgCreate)readerSystem.Serialization.Deserialize(_generatedBytes, 43, "a"),
                readerSystem, "registered generated reader");
        }
        ValidateBuffer(_legacyAdapter, _legacyBytes, "", _legacySystem);
        ValidateBuffer(_generated, _generatedBytes, "a", _generatedSystem);
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_generatedBytes.Length}B");
    }

    private static Config Settings(bool enabled) => ConfigurationFactory.ParseString(
        $"akka.actor.serialization-v2 = {(enabled ? "on" : "off")}");

    private void ValidateMessage(DaemonMsgCreate decoded, ExtendedActorSystem readerSystem, string context)
    {
        if (decoded.Props.Type != _message.Props.Type || !decoded.Props.Arguments.SequenceEqual(_message.Props.Arguments) ||
            decoded.Path != _message.Path || !decoded.Deploy.Equals(_message.Deploy) ||
            !decoded.Props.Deploy.Equals(_message.Props.Deploy) || !ReferenceEquals(decoded.Supervisor, readerSystem.DeadLetters))
            throw new InvalidOperationException($"Daemon benchmark failed {context}: actor/arguments/deploy/supervisor state differs.");
    }

    private void ValidateBuffer(SerializerV2 serializer, byte[] expectedBytes, string manifest, ExtendedActorSystem readerSystem)
    {
        var count = Write(serializer);
        if (count != _buffer.WrittenCount || !_buffer.WrittenSpan.SequenceEqual(expectedBytes))
            throw new InvalidOperationException("Daemon benchmark buffer output/count differs from its byte-array writer.");
        ValidateMessage((DaemonMsgCreate)serializer.Deserialize(new ReadOnlySequence<byte>(_buffer.WrittenMemory), manifest),
            readerSystem, "buffer sequence reader");
    }

    [GlobalCleanup]
    public async Task Cleanup() => await Task.WhenAll(_legacySystem.Terminate(), _generatedSystem.Terminate())
        .WaitAsync(TimeSpan.FromSeconds(10));

    [Benchmark] public byte[] ProtobufSerialize() => _legacy.ToBinary(_message);
    [Benchmark] public byte[] MessagePackSerialize() => _generated.ToBinary(_message);
    [Benchmark] public object ProtobufDeserialize() => _legacy.FromBinary(_legacyBytes, "");
    [Benchmark] public object MessagePackDeserialize() => _generated.FromBinary(_generatedBytes, "a");
    [Benchmark] public int ProtobufBufferWrite() => Write(_legacyAdapter);
    [Benchmark] public int MessagePackBufferWrite() => Write(_generated);

    private int Write(SerializerV2 serializer)
    {
        _buffer.Clear();
        return serializer.Serialize(_message, _buffer);
    }

    public sealed class BenchmarkActor : UntypedActor
    {
        public BenchmarkActor() { }
        public BenchmarkActor(string text, int number, object? optional) { }
        protected override void OnReceive(object message) => Sender.Tell(message);
    }
}
