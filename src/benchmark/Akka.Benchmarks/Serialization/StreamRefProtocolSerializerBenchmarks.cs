//-----------------------------------------------------------------------
// <copyright file="StreamRefProtocolSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Remote.Serialization;
using Akka.Streams.Serialization;
using Akka.Configuration;
using Akka.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

/// <summary>
/// Legacy id30 versus generated id70: byte-array writes, buffer writes, reads, allocations,
/// and printed wire sizes for next, demand, failure, handshake and bare generic references.
/// Uses public id dispatch so no benchmark-only production API or friend assembly is needed.
/// </summary>
[Config(typeof(MicroBenchmarkConfig))]
public class StreamRefProtocolSerializerBenchmarks
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

    [Params("Next", "Demand", "Failure", "Handshake", "SourceRef", "SinkRef")]
    public string Shape { get; set; } = "Next";

    [GlobalSetup]
    public void Setup()
    {
        // Bare refs resolve their partner through the receiving local provider. Matching names
        // keep that resolution equivalent in the actual readers timed below.
        _legacySystem = (ExtendedActorSystem)ActorSystem.Create("streams-bench", Config(false));
        _v2System = (ExtendedActorSystem)ActorSystem.Create("streams-bench", Config(true));
        // Reviewed pre-port protobuf records construct the private protocol shapes via the
        // unchanged public reader. No benchmark-only production API or friend assembly is used.
        var (manifest, hex) = Shape switch
        {
            "Next" => ("A", "12320a077061796c6f616410111a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
            "Demand" => ("B", "0801"),
            "Failure" => ("C", "0a0a6661696c656420e29883"),
            "Handshake" => ("G", "0a150a13616b6b613a2f2f616c6c2d73797374656d732f"),
            "SourceRef" => ("E", "0a0012270a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
            "SinkRef" => ("F", "0a0012270a2553797374656d2e537472696e672c2053797374656d2e507269766174652e436f72654c6962"),
            _ => throw new InvalidOperationException($"Unknown benchmark shape [{Shape}]")
        };
        _message = _legacySystem.Serialization.Deserialize(Convert.FromHexString(hex), 30, manifest);
        _legacy = _legacySystem.Serialization.FindSerializerFor(_message);
        _generated = (SerializerV2)_v2System.Serialization.FindSerializerFor(_message);
        _legacyAdapter = new SerializerV1Adapter(_legacySystem, _legacy);
        _manifest = Akka.Serialization.Serialization.ManifestFor(_legacy, _message);
        if (_legacy.Identifier != 30 || _generated.Identifier != 70 || _generated.Manifest(_message) != _manifest)
            throw new InvalidOperationException("Stream protocol benchmark selected the wrong ids or manifests.");
        _legacyBytes = ProtobufSerialize();
        _v2Bytes = MessagePackSerialize();
        var reader = new StreamRefSerializer(_legacySystem);
        foreach (var restored in new[]
                 {
                     ProtobufDeserialize(),
                     MessagePackDeserialize()
                 })
            Validate(restored, reader);
        foreach (var system in new[] { _legacySystem, _v2System })
        {
            Validate(system.Serialization.Deserialize(_v2Bytes, 70, _manifest), reader);
            Validate(system.Serialization.Deserialize(_legacyBytes, 30, _manifest), reader);
        }
        ValidateWriter(ProtobufBufferWrite(), _legacyBytes, _legacyAdapter, reader);
        ValidateWriter(MessagePackBufferWrite(), _v2Bytes, _generated, reader);
        Console.WriteLine($"{Shape}: protobuf={_legacyBytes.Length}B MessagePack={_v2Bytes.Length}B");
    }

    private void Validate(object restored, StreamRefSerializer reader)
    {
        if (restored.GetType() != _message.GetType() ||
            !reader.ToBinary(restored).AsSpan().SequenceEqual(reader.ToBinary(_message)))
            throw new InvalidOperationException("Stream protocol benchmark failed full-member validation.");
    }

    private void ValidateWriter(int count, byte[] expected, SerializerV2 serializer, StreamRefSerializer reader)
    {
        if (count != _buffer.WrittenCount || !_buffer.WrittenSpan.SequenceEqual(expected))
            throw new InvalidOperationException("Stream protocol benchmark failed buffer count or byte validation.");
        Validate(serializer.Deserialize(new ReadOnlySequence<byte>(_buffer.WrittenMemory), _manifest), reader);
    }

    private static Config Config(bool v2) => ConfigurationFactory.ParseString($$"""
        akka.actor.provider = local
        akka.actor.serialization-v2 = {{(v2 ? "on" : "off")}}
        """).WithFallback(ActorMaterializer.DefaultConfig()).WithFallback(ConfigurationFactory.FromResource<PrimitiveSerializers>("Akka.Remote.Configuration.Remote.conf"));

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
