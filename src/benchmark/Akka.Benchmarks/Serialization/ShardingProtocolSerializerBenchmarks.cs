//-----------------------------------------------------------------------
// <copyright file="ShardingProtocolSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Remote.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>Compare actual id 13/53 bindings across byte arrays, native buffer writes and reads.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ShardingProtocolSerializerBenchmarks
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

    [Params("Envelope", "CoordinatorState", "RegionStats", "EntitiesStarted")]
    public string Shape { get; set; } = "Envelope";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(true));
        // Reviewed pre-port protobuf records construct the private protocol shapes via the
        // unchanged public reader. The module reader supplies the production domain instance.
        var (manifest, hex) = Shape switch
        {
            "Envelope" => ("a", "0a02653112320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
            "CoordinatorState" => ("AA", "0a330a027331122d616b6b612e7463703a2f2f77697265403132372e302e302e313a323535322f757365722f726567696f6e233432122d616b6b612e7463703a2f2f77697265403132372e302e302e313a323535322f757365722f726567696f6e2334321a2c616b6b612e7463703a2f2f77697265403132372e302e302e313a323535322f757365722f70726f7879233432220b756e616c6c6f6361746564"),
            "RegionStats" => ("DD", "0a060a027331101112066661696c6564"),
            "EntitiesStarted" => ("CE", "0a026531"),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _message = _legacySystem.Serialization.Deserialize(Convert.FromHexString(hex), 13, manifest);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 13 || _generated.Identifier != 53 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Protocol benchmark selected the wrong ids or manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        if (!_legacyBytes.AsSpan().SequenceEqual(Convert.FromHexString(hex)))
            throw new InvalidOperationException("Frozen bootstrap changed protocol fields or actor-reference paths.");
        _v2Bytes = _generated.ToBinary(_message);
        var reader = _legacy;
        foreach (var restored in new[]
                 {
                     _legacySystem.Serialization.Deserialize(_v2Bytes, 53, _manifest),
                     _generated.FromBinary(_v2Bytes, _manifest),
                     _legacy.FromBinary(_legacyBytes, _manifest),
                     _legacySystem.Serialization.Deserialize(_legacyBytes, 13, _manifest)
                 })
        {
            if (restored.GetType() != _message.GetType() ||
                !reader.ToBinary(restored).AsSpan().SequenceEqual(reader.ToBinary(_message)))
                throw new InvalidOperationException("Protocol benchmark failed full-member validation.");
        }
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    private static Config Config(bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = remote
        akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
        akka.remote.dot-netty.tcp.port = 0
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        """).WithFallback(Akka.Cluster.Sharding.ClusterSharding.DefaultConfig()).WithFallback(ConfigurationFactory.FromResource<PrimitiveSerializers>("Akka.Remote.Configuration.Remote.conf"));

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
