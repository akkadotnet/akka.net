//-----------------------------------------------------------------------
// <copyright file="ClusterClientSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Cluster.Tools.Client;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// Legacy id15 versus generated id55: byte-array writes, buffer writes, reads, allocations,
/// and printed wire sizes for heartbeat, contact discovery, Send and Publish envelopes.
/// Uses public id dispatch so no benchmark-only production API or friend assembly is needed.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class ClusterClientSerializerBenchmarks
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

    [Params("Heartbeat", "Contacts", "Send", "Publish")]
    public string Shape { get; set; } = "Heartbeat";

    [GlobalSetup]
    public void Setup()
    {
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("client-bench-legacy", Config(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("client-bench-v2", Config(true));
        ClusterClientSettings.Create(_legacySystem);
        ClusterClientSettings.Create(_v2System);
        _message = Shape switch
        {
            "Heartbeat" => _legacySystem.Serialization.Deserialize(Array.Empty<byte>(), 15, "C"),
            // Frozen pre-port id15 Contacts fixture; exposes the private domain type through its real reader.
            "Contacts" => _legacySystem.Serialization.Deserialize(Convert.FromHexString(
                "0a3a616b6b612e7463703a2f2f636c69656e742d70726f6f66403132372e302e302e313a323535322f73797374656d2f726563657074696f6e697374"), 15, "A"),
            "Send" => new ClusterClient.Send("/user/worker", new string('x', 128), true),
            "Publish" => new ClusterClient.Publish("topic", new string('x', 128)),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 15 || _generated.Identifier != 55 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Cluster Client benchmark selected the wrong ids or manifests.");
        _legacyBytes = _legacy.ToBinary(_message);
        _v2Bytes = _generated.ToBinary(_message);
        if (!Equals(_legacySystem.Serialization.Deserialize(_v2Bytes, 55, _manifest), _message) ||
            !Equals(_v2System.Serialization.Deserialize(_legacyBytes, 15, _manifest), _message))
            throw new InvalidOperationException("Cluster Client benchmark corpus failed cross-id validation.");
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    private static Config Config(bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = local
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        akka.cluster.client.use-legacy-serialization = off
        """).WithFallback(ClusterClientReceptionist.DefaultConfig());

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
