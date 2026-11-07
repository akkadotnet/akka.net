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
    private ExtendedActorSystem _system = null!;
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
        _system = (ExtendedActorSystem)ActorSystem.Create("daemon-benchmark");
        _legacy = _system.Serialization.GetSerializerById(3);
        _generated = (SerializerV2)_system.Serialization.GetSerializerById(43);
        _legacyAdapter = new SerializerV1Adapter(_system, _legacy);
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
        _legacyBytes = _legacy.ToBinary(_message);
        _generatedBytes = _generated.ToBinary(_message);
        if (_legacy.Identifier != 3 || _legacy.Manifest(_message) != "" || _generated.Identifier != 43 || _generated.Manifest(_message) != "a")
            throw new InvalidOperationException("Incorrect daemon serializer ids/manifests.");
        foreach (var decoded in new[]
        {
            (DaemonMsgCreate)_system.Serialization.Deserialize(_legacyBytes, 3, ""),
            (DaemonMsgCreate)_system.Serialization.Deserialize(_generatedBytes, 43, "a")
        })
        {
            if (decoded.Props.Type != props.Type || !decoded.Props.Arguments.SequenceEqual(props.Arguments) ||
                decoded.Path != _message.Path || !decoded.Deploy.Equals(deploy) || !decoded.Props.Deploy.Equals(props.Deploy) ||
                decoded.Supervisor != _system.DeadLetters)
                throw new InvalidOperationException("Daemon benchmark corpus failed reader validation.");
        }
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_generatedBytes.Length}B");
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _system.Terminate().WaitAsync(TimeSpan.FromSeconds(10));

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
