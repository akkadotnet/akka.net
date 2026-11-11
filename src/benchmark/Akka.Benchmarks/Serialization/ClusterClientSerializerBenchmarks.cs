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
    private const string ContactsFixtureHex =
        "0a3a616b6b612e7463703a2f2f636c69656e742d70726f6f66403132372e302e302e313a323535322f73797374656d2f726563657074696f6e697374";
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
            "Contacts" => _legacySystem.Serialization.Deserialize(Convert.FromHexString(ContactsFixtureHex), 15, "A"),
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
        _legacyBytes = ProtobufSerialize();
        _v2Bytes = MessagePackSerialize();
        ValidateMessage(ProtobufDeserialize(), "legacy timed reader");
        ValidateMessage(MessagePackDeserialize(), "V2 timed reader");
        foreach (var system in new[] { _legacySystem, _v2System })
        {
            ValidateMessage(system.Serialization.Deserialize(_legacyBytes, 15, _manifest), "registered legacy reader");
            ValidateMessage(system.Serialization.Deserialize(_v2Bytes, 55, _manifest), "registered V2 reader");
        }
        ValidateWriter(ProtobufBufferWrite(), _legacyBytes, "legacy timed writer");
        ValidateWriter(MessagePackBufferWrite(), _v2Bytes, "V2 timed writer");
        if (Shape == "Contacts")
            ValidateContactOrder();
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    private void ValidateWriter(int count, byte[] expected, string contract)
    {
        Require(count == _buffer.WrittenCount, $"{contract}: reported byte count");
        Require(_buffer.WrittenSpan.SequenceEqual(expected), $"{contract}: array/buffer byte parity");
    }

    private void ValidateMessage(object actual, string contract)
    {
        Require(actual.GetType() == _message.GetType(), $"{contract}: root runtime type");
        switch (_message)
        {
            case ClusterClient.Send expected:
                var send = (ClusterClient.Send)actual;
                Require(send.Path == expected.Path && send.LocalAffinity == expected.LocalAffinity,
                    $"{contract}: Send fields");
                ValidatePayload(expected.Message, send.Message, contract);
                break;
            case ClusterClient.Publish expected:
                var publish = (ClusterClient.Publish)actual;
                Require(publish.Topic == expected.Topic, $"{contract}: Publish topic");
                ValidatePayload(expected.Message, publish.Message, contract);
                break;
            default:
                if (Shape == "Heartbeat")
                    Require(ReferenceEquals(actual, _message), $"{contract}: heartbeat singleton");
                else
                    Require(_legacy.ToBinary(actual).AsSpan().SequenceEqual(Convert.FromHexString(ContactsFixtureHex)),
                        $"{contract}: frozen Contacts fields");
                break;
        }
    }

    private void ValidatePayload(object expected, object actual, string contract)
    {
        Require(actual is string value && value == (string)expected, $"{contract}: complete string payload");
        var legacy = _legacySystem.Serialization.FindSerializerFor(expected);
        var enabled = _v2System.Serialization.FindSerializerFor(expected);
        Require(legacy.Identifier == enabled.Identifier
            && Akka.Serialization.Serialization.ManifestFor(legacy, expected)
            == Akka.Serialization.Serialization.ManifestFor(enabled, expected), $"{contract}: payload binding");
    }

    private void ValidateContactOrder()
    {
        // Add a distinct port to the frozen protobuf list without changing the timed input.
        var bytes = Convert.FromHexString(ContactsFixtureHex.Replace("32353532", "32353533", StringComparison.Ordinal)
            + ContactsFixtureHex);
        var contacts = _legacySystem.Serialization.Deserialize(bytes, 15, "A");
        var encoded = _generated.ToBinary(contacts);
        foreach (var system in new[] { _legacySystem, _v2System })
        {
            var restored = system.Serialization.Deserialize(encoded, 55, "A");
            Require(_legacy.ToBinary(restored).AsSpan().SequenceEqual(bytes), "Contacts list order");
        }
    }

    private static void Require(bool condition, string contract)
    {
        if (!condition)
            throw new InvalidOperationException($"Cluster Client benchmark setup failed: {contract}.");
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
