//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackSerializerBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Dsl;
using Akka.Actor.Setup;
using Akka.Benchmarks.Configurations;
using Akka.Serialization;
using Akka.Serialization.V2;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Serialization;

[MemoryDiagnoser]
[Config(typeof(MicroBenchmarkConfig))]
public class GeneratedMessagePackSerializerBenchmarks
{
    private ExtendedActorSystem _generatedSystem = null!;
    private ExtendedActorSystem _jsonSystem = null!;
    private SubmitOrder _message = null!;
    private Serializer _generatedSerializer = null!;
    private Serializer _envelopeSerializer = null!;
    private Serializer _jsonSerializer = null!;
    private CustomBenchmarkPayload _customPayload = null!;
    private BenchmarkEnvelope _generatedPayloadEnvelope = null!;
    private BenchmarkEnvelope _customPayloadEnvelope = null!;
    private BenchmarkOuterEnvelope _generatedNestedEnvelope = null!;
    private BenchmarkOuterEnvelope _customNestedEnvelope = null!;
    private byte[] _generatedBytes = null!;
    private byte[] _generatedPayloadEnvelopeBytes = null!;
    private byte[] _customPayloadEnvelopeBytes = null!;
    private byte[] _generatedNestedEnvelopeBytes = null!;
    private byte[] _customNestedEnvelopeBytes = null!;
    private byte[] _jsonBytes = null!;
    private string _generatedManifest = null!;
    private string _envelopeManifest = null!;
    private string _nestedEnvelopeManifest = null!;
    private string _jsonManifest = null!;

    [GlobalSetup]
    public void Setup()
    {
        var generatedSetup = ActorSystemSetup.Create(Akka.Serialization.SerializationSetup.Create(system =>
        {
            var order = OrderBenchmarkSerializer.CreateRegistration().CreateDetails(system);
            var envelope = EnvelopeBenchmarkSerializer.CreateRegistration().CreateDetails(system);
            var custom = SerializerDetails.Create(
                "custom-protobuf-benchmark",
                new CustomBenchmarkPayloadSerializer(system),
                ImmutableHashSet.Create<Type>(typeof(CustomBenchmarkPayload)));
            return ImmutableHashSet.Create(order, envelope, custom);
        }));
        _generatedSystem = (ExtendedActorSystem)ActorSystem.Create("generated-messagepack-bench", generatedSetup);
        _jsonSystem = (ExtendedActorSystem)ActorSystem.Create("json-messagepack-bench");

        var replyTo = _generatedSystem.ActorOf(act => act.ReceiveAny((message, context) => context.Sender.Tell(message)), "replyTo");
        _message = new SubmitOrder(
            "order-12345",
            Guid.Parse("964e7a77-4112-4f75-8fa4-5a2d8af546bb"),
            12500042L,
            true,
            1337.42m,
            DateTimeOffset.FromUnixTimeMilliseconds(1_735_689_600_000),
            OrderPriority.High,
            replyTo);
        _customPayload = new CustomBenchmarkPayload("custom-payload-12345", 1337);

        _generatedSerializer = _generatedSystem.Serialization.FindSerializerFor(_message);
        _envelopeSerializer = _generatedSystem.Serialization.FindSerializerFor(new BenchmarkEnvelope(
            "probe",
            new BenchmarkSerializedPayload(_generatedSerializer.Identifier, string.Empty, Array.Empty<byte>())));
        _jsonSerializer = _jsonSystem.Serialization.FindSerializerFor(_message);
        _generatedManifest = Akka.Serialization.Serialization.ManifestFor(_generatedSerializer, _message);
        _envelopeManifest = "benchmark-envelope-v1";
        _nestedEnvelopeManifest = "benchmark-outer-envelope-v1";
        _jsonManifest = Akka.Serialization.Serialization.ManifestFor(_jsonSerializer, _message);
        _generatedBytes = _generatedSystem.Serialization.Serialize(_message);
        _jsonBytes = _jsonSystem.Serialization.Serialize(_message);
        _generatedPayloadEnvelope = new BenchmarkEnvelope("generated-v2", CapturePayloadUsingV2BufferWhenAvailable(_generatedSystem, _message));
        _customPayloadEnvelope = new BenchmarkEnvelope("custom-v1", CapturePayloadUsingV2BufferWhenAvailable(_generatedSystem, _customPayload));
        _generatedNestedEnvelope = new BenchmarkOuterEnvelope("outer-generated", new BenchmarkInnerEnvelope("inner-generated", _message));
        _customNestedEnvelope = new BenchmarkOuterEnvelope("outer-custom", new BenchmarkInnerEnvelope("inner-custom", _customPayload));
        _generatedPayloadEnvelopeBytes = _generatedSystem.Serialization.Serialize(_generatedPayloadEnvelope);
        _customPayloadEnvelopeBytes = _generatedSystem.Serialization.Serialize(_customPayloadEnvelope);
        _generatedNestedEnvelopeBytes = _generatedSystem.Serialization.Serialize(_generatedNestedEnvelope);
        _customNestedEnvelopeBytes = _generatedSystem.Serialization.Serialize(_customNestedEnvelope);

        if (_generatedSerializer is not OrderBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated order serializer, got [{_generatedSerializer.GetType()}].");
        if (_envelopeSerializer is not EnvelopeBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated envelope serializer, got [{_envelopeSerializer.GetType()}].");

        Console.WriteLine($"Generated MessagePack payload size: {_generatedBytes.Length} bytes");
        Console.WriteLine($"Generated envelope with generated payload size: {_generatedPayloadEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Generated envelope with custom payload size: {_customPayloadEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Nested generated envelope with generated payload size: {_generatedNestedEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Nested generated envelope with custom payload size: {_customNestedEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Newtonsoft JSON payload size: {_jsonBytes.Length} bytes");
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        await Task.WhenAll(_generatedSystem.Terminate(), _jsonSystem.Terminate());
    }

    [Benchmark]
    public byte[] Generated_MessagePack_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_message);
    }

    [Benchmark]
    public object Generated_MessagePack_deserialize()
    {
        return _generatedSystem.Serialization.Deserialize(_generatedBytes, _generatedSerializer.Identifier, _generatedManifest);
    }

    [Benchmark]
    public byte[] Envelope_generated_payload_capture_ToBinary_serialize()
    {
        var envelope = new BenchmarkEnvelope("generated-v2", CapturePayloadUsingToBinary(_generatedSystem, _message));
        return _generatedSystem.Serialization.Serialize(envelope);
    }

    [Benchmark]
    public byte[] Envelope_generated_payload_capture_V2Buffer_serialize()
    {
        var envelope = new BenchmarkEnvelope("generated-v2", CapturePayloadUsingV2BufferWhenAvailable(_generatedSystem, _message));
        return _generatedSystem.Serialization.Serialize(envelope);
    }

    [Benchmark]
    public byte[] Envelope_generated_payload_precaptured_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_generatedPayloadEnvelope);
    }

    [Benchmark]
    public object Envelope_generated_payload_deserialize_and_recover()
    {
        var envelope = (BenchmarkEnvelope)_generatedSystem.Serialization.Deserialize(
            _generatedPayloadEnvelopeBytes,
            _envelopeSerializer.Identifier,
            _envelopeManifest);
        return RecoverPayload(_generatedSystem, envelope.Payload);
    }

    [Benchmark]
    public byte[] Envelope_custom_payload_capture_ToBinary_serialize()
    {
        var envelope = new BenchmarkEnvelope("custom-v1", CapturePayloadUsingToBinary(_generatedSystem, _customPayload));
        return _generatedSystem.Serialization.Serialize(envelope);
    }

    [Benchmark]
    public byte[] Envelope_custom_payload_capture_V2Buffer_serialize()
    {
        var envelope = new BenchmarkEnvelope("custom-v1", CapturePayloadUsingV2BufferWhenAvailable(_generatedSystem, _customPayload));
        return _generatedSystem.Serialization.Serialize(envelope);
    }

    [Benchmark]
    public byte[] Envelope_custom_payload_precaptured_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_customPayloadEnvelope);
    }

    [Benchmark]
    public object Envelope_custom_payload_deserialize_and_recover()
    {
        var envelope = (BenchmarkEnvelope)_generatedSystem.Serialization.Deserialize(
            _customPayloadEnvelopeBytes,
            _envelopeSerializer.Identifier,
            _envelopeManifest);
        return RecoverPayload(_generatedSystem, envelope.Payload);
    }

    [Benchmark]
    public byte[] NestedEnvelope_generated_payload_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_generatedNestedEnvelope);
    }

    [Benchmark]
    public object NestedEnvelope_generated_payload_deserialize_and_recover()
    {
        var envelope = (BenchmarkOuterEnvelope)_generatedSystem.Serialization.Deserialize(
            _generatedNestedEnvelopeBytes,
            _envelopeSerializer.Identifier,
            _nestedEnvelopeManifest);
        return envelope.Inner.Payload;
    }

    [Benchmark]
    public byte[] NestedEnvelope_custom_payload_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_customNestedEnvelope);
    }

    [Benchmark]
    public object NestedEnvelope_custom_payload_deserialize_and_recover()
    {
        var envelope = (BenchmarkOuterEnvelope)_generatedSystem.Serialization.Deserialize(
            _customNestedEnvelopeBytes,
            _envelopeSerializer.Identifier,
            _nestedEnvelopeManifest);
        return envelope.Inner.Payload;
    }

    [Benchmark(Baseline = true)]
    public byte[] NewtonsoftJson_serialize()
    {
        return _jsonSystem.Serialization.Serialize(_message);
    }

    [Benchmark]
    public object NewtonsoftJson_deserialize()
    {
        return _jsonSystem.Serialization.Deserialize(_jsonBytes, _jsonSerializer.Identifier, _jsonManifest);
    }

    public int Generated_MessagePack_payload_size()
    {
        return _generatedBytes.Length;
    }

    public int NewtonsoftJson_payload_size()
    {
        return _jsonBytes.Length;
    }

    private static BenchmarkSerializedPayload CapturePayloadUsingToBinary(ExtendedActorSystem system, object payload)
    {
        var serializer = system.Serialization.FindSerializerFor(payload);
        var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, payload);
        return new BenchmarkSerializedPayload(serializer.Identifier, manifest, serializer.ToBinary(payload));
    }

    private static BenchmarkSerializedPayload CapturePayloadUsingV2BufferWhenAvailable(ExtendedActorSystem system, object payload)
    {
        var serializer = system.Serialization.FindSerializerFor(payload);
        var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, payload);
        if (serializer is not SerializerV2 serializerV2)
            return new BenchmarkSerializedPayload(serializer.Identifier, manifest, serializer.ToBinary(payload));

        var sizeHint = serializerV2.SizeHint(payload);
        var writer = sizeHint > 0 ? new ArrayBufferWriter<byte>(sizeHint) : new ArrayBufferWriter<byte>();
        var bytesWritten = serializerV2.Serialize(payload, writer);
        if (bytesWritten != writer.WrittenCount)
            throw new SerializationException($"Serializer [{serializer.GetType()}] reported [{bytesWritten}] bytes but wrote [{writer.WrittenCount}] bytes.");

        return new BenchmarkSerializedPayload(serializer.Identifier, manifest, writer.WrittenMemory.ToArray());
    }

    private static object RecoverPayload(ExtendedActorSystem system, BenchmarkSerializedPayload payload)
    {
        return system.Serialization.Deserialize(payload.Bytes, payload.SerializerId, payload.Manifest);
    }
}

public interface IOrderBenchmarkProtocol
{
}

[AkkaSerializer(Name = "order-benchmark", SerializerId = 120001)]
public sealed partial class OrderBenchmarkSerializer : MessagePackSerializer<IOrderBenchmarkProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

public interface IEnvelopeBenchmarkProtocol
{
}

[AkkaSerializer(Name = "envelope-benchmark", SerializerId = 120002)]
public sealed partial class EnvelopeBenchmarkSerializer : MessagePackSerializer<IEnvelopeBenchmarkProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

[AkkaSerializable(Manifest = "submit-order-v1")]
public sealed record SubmitOrder(
    [property: AkkaField(0)] string OrderId,
    [property: AkkaField(1)] Guid CustomerId,
    [property: AkkaField(2)] long SequenceNr,
    [property: AkkaField(3)] bool Expedited,
    [property: AkkaField(4)] decimal Total,
    [property: AkkaField(5)] DateTimeOffset CreatedAt,
    [property: AkkaField(6)] OrderPriority Priority,
    [property: AkkaField(7)] IActorRef? ReplyTo) : IOrderBenchmarkProtocol;

[AkkaSerializable(Manifest = "benchmark-envelope-v1")]
public sealed record BenchmarkEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1)] BenchmarkSerializedPayload Payload) : IEnvelopeBenchmarkProtocol;

[AkkaSerializable]
public sealed record BenchmarkSerializedPayload(
    [property: AkkaField(0)] int SerializerId,
    [property: AkkaField(1)] string Manifest,
    [property: AkkaField(2)] byte[] Bytes);

[AkkaSerializable(Manifest = "benchmark-outer-envelope-v1")]
public sealed record BenchmarkOuterEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1), AkkaEnvelopePayload] BenchmarkInnerEnvelope Inner) : IEnvelopeBenchmarkProtocol;

[AkkaSerializable(Manifest = "benchmark-inner-envelope-v1")]
public sealed record BenchmarkInnerEnvelope(
    [property: AkkaField(0)] string EnvelopeId,
    [property: AkkaField(1), AkkaEnvelopePayload] object Payload) : IEnvelopeBenchmarkProtocol;

public sealed record CustomBenchmarkPayload(string PayloadId, int Value);

public sealed class CustomBenchmarkPayloadSerializer : SerializerWithStringManifest
{
    public const int IdentifierValue = 120003;
    public const string ManifestName = "custom-protobuf-benchmark-v1";

    public CustomBenchmarkPayloadSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => IdentifierValue;

    public override string Manifest(object o)
    {
        return o switch
        {
            CustomBenchmarkPayload => ManifestName,
            _ => throw new ArgumentException($"Unsupported benchmark payload type: {o.GetType()}", nameof(o))
        };
    }

    public override byte[] ToBinary(object obj)
    {
        if (obj is not CustomBenchmarkPayload payload)
            throw new ArgumentException($"Unsupported benchmark payload type: {obj.GetType()}", nameof(obj));

        return Encoding.UTF8.GetBytes($"fake-protobuf|{payload.PayloadId}|{payload.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest != ManifestName)
            throw new SerializationException($"Unknown benchmark payload manifest [{manifest}].");

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 3 || parts[0] != "fake-protobuf")
            throw new SerializationException("Invalid benchmark payload bytes.");

        return new CustomBenchmarkPayload(parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}

public enum OrderPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}
