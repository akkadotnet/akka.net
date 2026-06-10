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
using MessagePack;

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
    private CustomSubmitOrderPayload _customMessagePayload = null!;
    private BenchmarkEnvelope _generatedPayloadEnvelope = null!;
    private BenchmarkEnvelope _customPayloadEnvelope = null!;
    private BenchmarkOuterEnvelope _generatedNestedEnvelope = null!;
    private BenchmarkOuterEnvelope _customNestedEnvelope = null!;
    private BenchmarkOuterEnvelope _customSameShapeNestedEnvelope = null!;
    private byte[] _generatedBytes = null!;
    private byte[] _generatedPayloadEnvelopeBytes = null!;
    private byte[] _customPayloadEnvelopeBytes = null!;
    private byte[] _generatedNestedEnvelopeBytes = null!;
    private byte[] _customNestedEnvelopeBytes = null!;
    private byte[] _customSameShapeNestedEnvelopeBytes = null!;
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
                ImmutableHashSet.Create<Type>(typeof(CustomBenchmarkPayload), typeof(CustomSubmitOrderPayload)));
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
        _customMessagePayload = new CustomSubmitOrderPayload(
            _message.OrderId,
            _message.CustomerId,
            _message.SequenceNr,
            _message.Expedited,
            _message.Total,
            _message.CreatedAt,
            _message.Priority,
            _message.ReplyTo);

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
        _customSameShapeNestedEnvelope = new BenchmarkOuterEnvelope("outer-custom-same-shape", new BenchmarkInnerEnvelope("inner-custom-same-shape", _customMessagePayload));
        _generatedPayloadEnvelopeBytes = _generatedSystem.Serialization.Serialize(_generatedPayloadEnvelope);
        _customPayloadEnvelopeBytes = _generatedSystem.Serialization.Serialize(_customPayloadEnvelope);
        _generatedNestedEnvelopeBytes = _generatedSystem.Serialization.Serialize(_generatedNestedEnvelope);
        _customNestedEnvelopeBytes = _generatedSystem.Serialization.Serialize(_customNestedEnvelope);
        _customSameShapeNestedEnvelopeBytes = _generatedSystem.Serialization.Serialize(_customSameShapeNestedEnvelope);

        if (_generatedSerializer is not OrderBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated order serializer, got [{_generatedSerializer.GetType()}].");
        if (_envelopeSerializer is not EnvelopeBenchmarkSerializer)
            throw new InvalidOperationException($"Expected generated envelope serializer, got [{_envelopeSerializer.GetType()}].");

        Console.WriteLine($"Generated MessagePack payload size: {_generatedBytes.Length} bytes");
        Console.WriteLine($"Generated envelope with generated payload size: {_generatedPayloadEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Generated envelope with custom payload size: {_customPayloadEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Nested generated envelope with generated payload size: {_generatedNestedEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Nested generated envelope with custom payload size: {_customNestedEnvelopeBytes.Length} bytes");
        Console.WriteLine($"Nested generated envelope with custom same-shape payload size: {_customSameShapeNestedEnvelopeBytes.Length} bytes");
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

    [Benchmark]
    public byte[] NestedEnvelope_custom_same_shape_payload_serialize()
    {
        return _generatedSystem.Serialization.Serialize(_customSameShapeNestedEnvelope);
    }

    [Benchmark]
    public object NestedEnvelope_custom_same_shape_payload_deserialize_and_recover()
    {
        var envelope = (BenchmarkOuterEnvelope)_generatedSystem.Serialization.Deserialize(
            _customSameShapeNestedEnvelopeBytes,
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

public sealed record CustomSubmitOrderPayload(
    string OrderId,
    Guid CustomerId,
    long SequenceNr,
    bool Expedited,
    decimal Total,
    DateTimeOffset CreatedAt,
    OrderPriority Priority,
    IActorRef? ReplyTo);

public sealed class CustomBenchmarkPayloadSerializer : SerializerWithStringManifest
{
    public const int IdentifierValue = 120003;
    public const string ManifestName = "custom-protobuf-benchmark-v1";
    public const string SubmitOrderManifestName = "custom-submit-order-benchmark-v1";

    public CustomBenchmarkPayloadSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => IdentifierValue;

    public override string Manifest(object o)
    {
        return o switch
        {
            CustomBenchmarkPayload => ManifestName,
            CustomSubmitOrderPayload => SubmitOrderManifestName,
            _ => throw new ArgumentException($"Unsupported benchmark payload type: {o.GetType()}", nameof(o))
        };
    }

    public override byte[] ToBinary(object obj)
    {
        return obj switch
        {
            CustomBenchmarkPayload payload => Encoding.UTF8.GetBytes($"fake-protobuf|{payload.PayloadId}|{payload.Value.ToString(CultureInfo.InvariantCulture)}"),
            CustomSubmitOrderPayload payload => SerializeSubmitOrder(payload),
            _ => throw new ArgumentException($"Unsupported benchmark payload type: {obj.GetType()}", nameof(obj))
        };
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest == SubmitOrderManifestName)
            return DeserializeSubmitOrder(bytes);

        if (manifest != ManifestName)
            throw new SerializationException($"Unknown benchmark payload manifest [{manifest}].");

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 3 || parts[0] != "fake-protobuf")
            throw new SerializationException("Invalid benchmark payload bytes.");

        return new CustomBenchmarkPayload(parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    private static byte[] SerializeSubmitOrder(CustomSubmitOrderPayload payload)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(8);
        writer.Write(0);
        writer.Write(payload.OrderId);
        writer.Write(1);
        WriteGuid(ref writer, payload.CustomerId);
        writer.Write(2);
        writer.Write(payload.SequenceNr);
        writer.Write(3);
        writer.Write(payload.Expedited);
        writer.Write(4);
        WriteDecimal(ref writer, payload.Total);
        writer.Write(5);
        WriteDateTimeOffset(ref writer, payload.CreatedAt);
        writer.Write(6);
        writer.Write((int)payload.Priority);
        writer.Write(7);
        writer.Write(Akka.Serialization.Serialization.SerializedActorPath(payload.ReplyTo));
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private CustomSubmitOrderPayload DeserializeSubmitOrder(byte[] bytes)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        var fieldCount = reader.ReadMapHeader();
        string? orderId = null;
        var customerId = Guid.Empty;
        var sequenceNr = 0L;
        var expedited = false;
        var total = 0m;
        var createdAt = default(DateTimeOffset);
        var priority = OrderPriority.Low;
        IActorRef? replyTo = ActorRefs.NoSender;

        for (var entryIndex = 0; entryIndex < fieldCount; entryIndex++)
        {
            var fieldId = reader.ReadInt32();
            switch (fieldId)
            {
                case 0:
                    orderId = reader.ReadString();
                    break;
                case 1:
                    customerId = ReadGuid(ref reader);
                    break;
                case 2:
                    sequenceNr = reader.ReadInt64();
                    break;
                case 3:
                    expedited = reader.ReadBoolean();
                    break;
                case 4:
                    total = ReadDecimal(ref reader);
                    break;
                case 5:
                    createdAt = ReadDateTimeOffset(ref reader);
                    break;
                case 6:
                    priority = (OrderPriority)reader.ReadInt32();
                    break;
                case 7:
                    var path = reader.ReadString();
                    replyTo = string.IsNullOrEmpty(path) ? ActorRefs.NoSender : system.Provider.ResolveActorRef(path);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        return new CustomSubmitOrderPayload(orderId!, customerId, sequenceNr, expedited, total, createdAt, priority, replyTo);
    }

    private static Guid ReadGuid(ref MessagePackReader reader)
    {
        var bytes = reader.ReadBytes();
        if (bytes == null || bytes.Value.Length != 16)
            throw new SerializationException($"Expected 16 bytes for Guid, got {bytes?.Length ?? 0}.");

        Span<byte> span = stackalloc byte[16];
        bytes.Value.CopyTo(span);
        return new Guid(span);
    }

    private static void WriteGuid(ref MessagePackWriter writer, Guid value)
    {
        writer.WriteBinHeader(16);
        value.TryWriteBytes(writer.GetSpan(16));
        writer.Advance(16);
    }

    private static decimal ReadDecimal(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 4)
            throw new SerializationException($"Expected decimal array with 4 elements, got {arrayLength}.");

        var lo = reader.ReadInt32();
        var mid = reader.ReadInt32();
        var hi = reader.ReadInt32();
        var flags = reader.ReadInt32();
        return new decimal(new[] { lo, mid, hi, flags });
    }

    private static void WriteDecimal(ref MessagePackWriter writer, decimal value)
    {
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        writer.WriteArrayHeader(4);
        writer.Write(bits[0]);
        writer.Write(bits[1]);
        writer.Write(bits[2]);
        writer.Write(bits[3]);
    }

    private static DateTimeOffset ReadDateTimeOffset(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        if (arrayLength != 2)
            throw new SerializationException($"Expected DateTimeOffset array with 2 elements, got {arrayLength}.");

        var ticks = reader.ReadInt64();
        var offsetMinutes = reader.ReadInt32();
        return new DateTimeOffset(ticks, TimeSpan.FromMinutes(offsetMinutes));
    }

    private static void WriteDateTimeOffset(ref MessagePackWriter writer, DateTimeOffset value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Offset.TotalMinutes);
    }
}

public enum OrderPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}
