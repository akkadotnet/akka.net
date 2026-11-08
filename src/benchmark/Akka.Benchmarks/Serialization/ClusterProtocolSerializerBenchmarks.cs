//-----------------------------------------------------------------------
// <copyright file="ClusterProtocolSerializerBenchmarks.cs" company="Akka.NET Project">
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

/// <summary>Compare actual id 5/45 bindings across byte arrays, native buffer writes and reads.</summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ClusterProtocolSerializerBenchmarks
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

    [Params("Heartbeat", "Join", "GossipEnvelope", "RouterPool")]
    public string Shape { get; set; } = "Heartbeat";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("protocol-bench", Config(true));
        // Reviewed pre-port protobuf records construct the private protocol shapes via the
        // unchanged public reader. The module reader supplies the production domain instance.
        var (manifest, hex) = Shape switch
        {
            "Heartbeat" => ("HB", "0a1d0a07636c757374657212056e6f64653118f8132208616b6b612e746370101118a413"),
            "Join" => ("Akka.Cluster.InternalClusterAction+Join, Akka.Cluster", "0a260a1d0a07636c757374657212056e6f64653118f8132208616b6b612e7463701091808080802012076261636b656e641a05312e322e33"),
            "GossipEnvelope" => ("Akka.Cluster.GossipEnvelope, Akka.Cluster", "0a260a1d0a07636c757374657212056e6f64653118f8132208616b6b612e7463701091808080802012290a1d0a07636c757374657212056e6f64653218f9132208616b6b612e74637010feffffffffffffff7f1ace010a260a1d0a07636c757374657212056e6f64653118f8132208616b6b612e746370109180808080200a290a1d0a07636c757374657212056e6f64653218f9132208616b6b612e74637010feffffffffffffff7f0a290a1d0a07636c757374657212056e6f64653218f9132208616b6b612e74637010fdffffffffffffff7f12076261636b656e641a0a66697865642d6861736822071007180122010022090801100b18022201002a0f0a0100120a1206080118012013201732041202101d3a07080210959aef3a4205312e322e33"),
            "RouterPool" => ("Akka.Cluster.Routing.ClusterRouterPool, Akka.Cluster", "0a2c08101205524f5252501a210804121d616b6b612e6163746f722e64656661756c742d64697370617463686572120f080d1005180122076261636b656e64"),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _message = _legacySystem.Serialization.Deserialize(Convert.FromHexString(hex), 5, manifest);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 5 || _generated.Identifier != 45 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Protocol benchmark selected the wrong ids or manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        if (!_legacyBytes.AsSpan().SequenceEqual(Convert.FromHexString(hex)))
            throw new InvalidOperationException("Frozen bootstrap changed protocol fields or actor-reference paths.");
        _v2Bytes = _generated.ToBinary(_message);
        var reader = _legacy;
        foreach (var restored in new[]
                 {
                     _legacySystem.Serialization.Deserialize(_v2Bytes, 45, _manifest),
                     _generated.FromBinary(_v2Bytes, _manifest),
                     _legacy.FromBinary(_legacyBytes, _manifest),
                     _legacySystem.Serialization.Deserialize(_legacyBytes, 5, _manifest)
                 })
        {
            if (restored.GetType() != _message.GetType() ||
                !reader.ToBinary(restored).AsSpan().SequenceEqual(reader.ToBinary(_message)))
                throw new InvalidOperationException("Protocol benchmark failed full-member validation.");
        }
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    private static Config Config(bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = cluster
        akka.remote.dot-netty.tcp.hostname = "127.0.0.1"
        akka.remote.dot-netty.tcp.port = 0
        akka.cluster.use-legacy-heartbeat-message = off
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        """).WithFallback(ConfigurationFactory.FromResource<Akka.Cluster.Cluster>("Akka.Cluster.Configuration.Cluster.conf")).WithFallback(ConfigurationFactory.FromResource<PrimitiveSerializers>("Akka.Remote.Configuration.Remote.conf"));

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
