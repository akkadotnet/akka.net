//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackSerializerSpec.cs" company="Akka.NET Project">
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
using Akka.Actor.Setup;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

public sealed class GeneratedMessagePackSerializerSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private GeneratedTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("generated-messagepack-serializer-spec");
        _serializer = new GeneratedTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "MessagePack conventions should round-trip supported built-in field types")]
    public void MessagePack_conventions_should_round_trip_supported_built_in_field_types()
    {
        var id = Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4");
        var timestamp = new DateTime(2026, 6, 3, 4, 45, 0, DateTimeKind.Utc);
        var timestampOffset = new DateTimeOffset(2026, 6, 3, 4, 45, 0, TimeSpan.FromHours(2));
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);

        writer.WriteMapHeader(9);
        writer.Write(1);
        writer.Write("alpha");
        writer.Write(2);
        writer.Write(42);
        writer.Write(3);
        writer.Write(9000000000L);
        writer.Write(4);
        writer.Write(true);
        writer.Write(5);
        writer.Write(12.5d);
        writer.Write(6);
        WriteDecimal(ref writer, 123.456m);
        writer.Write(7);
        WriteGuid(ref writer, id);
        writer.Write(8);
        WriteDateTime(ref writer, timestamp);
        writer.Write(9);
        WriteDateTimeOffset(ref writer, timestampOffset);
        writer.Flush();

        var reader = new MessagePackReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        reader.ReadMapHeader().Should().Be(9);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("alpha");
        reader.ReadInt32().Should().Be(2);
        reader.ReadInt32().Should().Be(42);
        reader.ReadInt32().Should().Be(3);
        reader.ReadInt64().Should().Be(9000000000L);
        reader.ReadInt32().Should().Be(4);
        reader.ReadBoolean().Should().BeTrue();
        reader.ReadInt32().Should().Be(5);
        reader.ReadDouble().Should().Be(12.5d);
        reader.ReadInt32().Should().Be(6);
        ReadDecimal(ref reader).Should().Be(123.456m);
        reader.ReadInt32().Should().Be(7);
        ReadGuid(ref reader).Should().Be(id);
        reader.ReadInt32().Should().Be(8);
        ReadDateTime(ref reader).Should().Be(timestamp);
        reader.ReadInt32().Should().Be(9);
        ReadDateTimeOffset(ref reader).Should().Be(timestampOffset);
        reader.Consumed.Should().Be(buffer.WrittenCount);
    }

    [Fact(DisplayName = "Generated serializer should round-trip supported built-in field types")]
    public void Generated_serializer_should_round_trip_supported_built_in_field_types()
    {
        var message = new PrimitiveMessage(
            "order-1",
            42,
            9000000000L,
            true,
            12.5d,
            123.456m,
            Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4"),
            new DateTime(2026, 6, 3, 4, 45, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 6, 3, 4, 45, 0, TimeSpan.FromHours(2)),
            SampleStatus.Accepted,
            ActorRefs.NoSender);

        RoundTrip(message).Should().Be(message);
    }

    [Fact(DisplayName = "Generated serializer should write explicit field-id maps")]
    public void Generated_serializer_should_write_explicit_field_id_maps()
    {
        var message = new SparseFieldMessage(17, "alpha");
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(2);
        reader.ReadInt32().Should().Be(17);
        reader.ReadInt32().Should().Be(10);
        reader.ReadString().Should().Be("alpha");
    }

    [Fact(DisplayName = "Generated serializer should skip unknown field IDs")]
    public void Generated_serializer_should_skip_unknown_field_ids()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write(99);
        writer.Write("ignored");
        writer.Write(1);
        writer.Write("order-1");
        writer.Write(2);
        writer.Write(42);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), RequiredMessage.ManifestName);

        deserialized.Should().Be(new RequiredMessage("order-1", 42));
    }

    [Fact(DisplayName = "Generated serializer should skip unknown nested field values")]
    public void Generated_serializer_should_skip_unknown_nested_field_values()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(3);
        writer.Write(99);
        writer.WriteMapHeader(2);
        writer.Write(1);
        writer.Write("ignored");
        writer.Write(2);
        writer.WriteArrayHeader(2);
        writer.Write("also-ignored");
        writer.Write(17);
        writer.Write(1);
        writer.Write("order-1");
        writer.Write(2);
        writer.Write(42);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), RequiredMessage.ManifestName);

        deserialized.Should().Be(new RequiredMessage("order-1", 42));
    }

    [Fact(DisplayName = "Generated serializer should round-trip nullable value fields")]
    public void Generated_serializer_should_round_trip_nullable_value_fields()
    {
        var withValues = new OptionalMessage(
            "optional-1",
            42,
            Guid.Parse("78055b71-1e7a-4a20-8e52-712db4fda457"),
            new DateTime(2026, 6, 6, 12, 30, 0, DateTimeKind.Utc),
            SampleStatus.Accepted,
            "notes",
            new ShippingAddress("1 Main St", "Seattle"));
        var withoutValues = new OptionalMessage("optional-2", null, null, null, null, null, null);

        RoundTrip(withValues).Should().Be(withValues);
        RoundTrip(withoutValues).Should().Be(withoutValues);
    }

    [Fact(DisplayName = "Generated serializer should write nil for nullable fields")]
    public void Generated_serializer_should_write_nil_for_nullable_fields()
    {
        var message = new OptionalMessage("optional-1", null, null, null, null, null, null);
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(7);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("optional-1");
        for (var fieldId = 2; fieldId <= 7; fieldId++)
        {
            reader.ReadInt32().Should().Be(fieldId);
            reader.TryReadNil().Should().BeTrue();
        }

        reader.Consumed.Should().Be(bytes.Length);
    }

    [Fact(DisplayName = "Generated serializer should allow missing nullable fields")]
    public void Generated_serializer_should_allow_missing_nullable_fields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        writer.Write("optional-1");
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), OptionalMessage.ManifestName);

        deserialized.Should().Be(new OptionalMessage("optional-1", null, null, null, null, null, null));
    }

    [Fact(DisplayName = "Generated serializer should reject missing non-nullable required fields")]
    public void Generated_serializer_should_reject_missing_non_nullable_required_fields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(2);
        writer.Write(42);
        writer.Flush();

        Action deserialize = () => _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), RequiredMessage.ManifestName);

        deserialize.Should().Throw<SerializationException>()
            .WithMessage("*Missing required field [Name] with index [1]*");
    }

    [Fact(DisplayName = "Generated serializer should report bytes written")]
    public void Generated_serializer_should_report_bytes_written()
    {
        var message = new RequiredMessage("order-1", 42);
        var buffer = new ArrayBufferWriter<byte>();

        var bytesWritten = _serializer.Serialize(message, buffer);

        bytesWritten.Should().Be(buffer.WrittenCount);
        bytesWritten.Should().BeGreaterThan(0);
    }

    [Fact(DisplayName = "Generated serializer should report exact size hints for exact schemas")]
    public void Generated_serializer_should_report_exact_size_hints_for_exact_schemas()
    {
        var required = new RequiredMessage("order-1", 42);
        var primitive = new PrimitiveMessage(
            "order-1",
            42,
            9000000000L,
            true,
            12.5d,
            123.456m,
            Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4"),
            new DateTime(2026, 6, 3, 4, 45, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 6, 3, 4, 45, 0, TimeSpan.FromHours(2)),
            SampleStatus.Accepted,
            ActorRefs.NoSender);
        var optional = new OptionalMessage(
            "optional-1",
            42,
            Guid.Parse("78055b71-1e7a-4a20-8e52-712db4fda457"),
            new DateTime(2026, 6, 6, 12, 30, 0, DateTimeKind.Utc),
            SampleStatus.Accepted,
            "notes",
            new ShippingAddress("1 Main St", "Seattle"));
        var optionalNulls = new OptionalMessage("optional-2", null, null, null, null, null, null);
        var envelope = new OpaqueEnvelope(
            "envelope-1",
            new OpaqueSerializedPayload(
                CustomProtobufPayloadSerializer.IdentifierValue,
                CustomProtobufPayloadSerializer.ManifestName,
                Encoding.UTF8.GetBytes("fake-protobuf|payload-1|17")));

        _serializer.SizeHint(required).Should().Be(_serializer.ToBinary(required).Length);
        _serializer.SizeHint(primitive).Should().Be(_serializer.ToBinary(primitive).Length);
        _serializer.SizeHint(optional).Should().Be(_serializer.ToBinary(optional).Length);
        _serializer.SizeHint(optionalNulls).Should().Be(_serializer.ToBinary(optionalNulls).Length);
        _serializer.SizeHint(envelope).Should().Be(_serializer.ToBinary(envelope).Length);
    }

    [Fact(DisplayName = "Generated serializer should use manifest dispatch")]
    public void Generated_serializer_should_use_manifest_dispatch()
    {
        var message = new RequiredMessage("order-1", 42);
        var bytes = _serializer.ToBinary(message);

        _serializer.Manifest(message).Should().Be(RequiredMessage.ManifestName);
        Action deserialize = () => _serializer.FromBinary(bytes, "unknown-v1");
        deserialize.Should().Throw<SerializationException>()
            .WithMessage("*Unknown generated serializer manifest [unknown-v1]*");
    }

    [Fact(DisplayName = "Generated serializer should round-trip through Serialization")]
    public async Task Generated_serializer_should_round_trip_through_Serialization()
    {
        var setup = ActorSystemSetup.Create(GeneratedTestSerializer.CreateRegistration().CreateSetup());
        var system = ActorSystem.Create("generated-messagepack-serialization-spec", setup);
        try
        {
            var message = new RequiredMessage("order-1", 42);
            var serializer = system.Serialization.FindSerializerFor(message);
            serializer.Should().BeOfType<GeneratedTestSerializer>();

            var bytes = system.Serialization.Serialize(message);
            var manifest = Akka.Serialization.Serialization.ManifestFor(serializer, message);
            var deserialized = system.Serialization.Deserialize(bytes, serializer.Identifier, manifest);

            deserialized.Should().Be(message);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Generated MessagePack wrapper should preserve opaque custom serializer payload")]
    public async Task Generated_MessagePack_wrapper_should_preserve_opaque_custom_serializer_payload()
    {
        var setup = ActorSystemSetup.Create(global::Akka.Serialization.SerializationSetup.Create(extendedSystem =>
        {
            var generated = GeneratedTestSerializer.CreateRegistration().CreateDetails(extendedSystem);
            var custom = global::Akka.Serialization.SerializerDetails.Create(
                "custom-protobuf",
                new CustomProtobufPayloadSerializer(extendedSystem),
                ImmutableHashSet.Create<Type>(typeof(CustomProtobufPayload)));
            return ImmutableHashSet.Create(generated, custom);
        }));
        var system = ActorSystem.Create("generated-messagepack-opaque-payload-spec", setup);
        try
        {
            var extendedSystem = (ExtendedActorSystem)system;
            var innerPayload = new CustomProtobufPayload("payload-1", 17);
            typeof(CustomProtobufPayload).GetCustomAttributes(typeof(AkkaSerializableAttribute), false).Should().BeEmpty();

            var innerSerializer = system.Serialization.FindSerializerFor(innerPayload);
            innerSerializer.Should().BeOfType<CustomProtobufPayloadSerializer>();
            var expectedInnerBytes = innerSerializer.ToBinary(innerPayload);
            var opaquePayload = CaptureOpaquePayload(extendedSystem, innerPayload);
            opaquePayload.SerializerId.Should().Be(CustomProtobufPayloadSerializer.IdentifierValue);
            opaquePayload.Manifest.Should().Be(CustomProtobufPayloadSerializer.ManifestName);
            opaquePayload.Bytes.Should().Equal(expectedInnerBytes);

            var envelope = new OpaqueEnvelope("envelope-1", opaquePayload);
            var envelopeSerializer = system.Serialization.FindSerializerFor(envelope);
            envelopeSerializer.Should().BeOfType<GeneratedTestSerializer>();
            var envelopeBytes = system.Serialization.Serialize(envelope);

            AssertOpaqueEnvelopeBytes(envelopeBytes, expectedInnerBytes);

            var envelopeManifest = global::Akka.Serialization.Serialization.ManifestFor(envelopeSerializer, envelope);
            var recoveredEnvelope = system.Serialization.Deserialize(envelopeBytes, envelopeSerializer.Identifier, envelopeManifest)
                .Should().BeOfType<OpaqueEnvelope>().Subject;
            recoveredEnvelope.EnvelopeId.Should().Be("envelope-1");
            recoveredEnvelope.Payload.SerializerId.Should().Be(CustomProtobufPayloadSerializer.IdentifierValue);
            recoveredEnvelope.Payload.Manifest.Should().Be(CustomProtobufPayloadSerializer.ManifestName);
            recoveredEnvelope.Payload.Bytes.Should().Equal(expectedInnerBytes);

            var recoveredInnerPayload = RecoverOpaquePayload(extendedSystem, recoveredEnvelope.Payload);
            recoveredInnerPayload.Should().Be(innerPayload);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Generated serializer should round-trip nested envelope payloads through Akka serialization")]
    public async Task Generated_serializer_should_round_trip_nested_envelope_payloads_through_Akka_serialization()
    {
        var setup = ActorSystemSetup.Create(global::Akka.Serialization.SerializationSetup.Create(extendedSystem =>
        {
            var generated = GeneratedTestSerializer.CreateRegistration().CreateDetails(extendedSystem);
            var custom = global::Akka.Serialization.SerializerDetails.Create(
                "custom-protobuf",
                new CustomProtobufPayloadSerializer(extendedSystem),
                ImmutableHashSet.Create<Type>(typeof(CustomProtobufPayload)));
            return ImmutableHashSet.Create(generated, custom);
        }));
        var system = ActorSystem.Create("generated-messagepack-nested-envelope-spec", setup);
        try
        {
            var customPayload = new CustomProtobufPayload("payload-1", 17);
            var generatedPayload = new RequiredMessage("order-1", 42);
            var customEnvelope = new AttributeOuterEnvelope("outer-custom", new AttributeInnerEnvelope("inner-custom", customPayload));
            var generatedEnvelope = new AttributeOuterEnvelope("outer-generated", new AttributeInnerEnvelope("inner-generated", generatedPayload));
            var envelopeSerializer = system.Serialization.FindSerializerFor(generatedEnvelope)
                .Should().BeAssignableTo<global::Akka.Serialization.SerializerV2>().Subject;

            var customRecovered = RoundTripThroughSerialization<AttributeOuterEnvelope>(system, customEnvelope);
            customRecovered.Should().Be(customEnvelope);
            customRecovered.Inner.Payload.Should().BeOfType<CustomProtobufPayload>();
            envelopeSerializer.SizeHint(customEnvelope).Should().Be(global::Akka.Serialization.SerializerV2.UnknownSize);

            var generatedRecovered = RoundTripThroughSerialization<AttributeOuterEnvelope>(system, generatedEnvelope);
            generatedRecovered.Should().Be(generatedEnvelope);
            generatedRecovered.Inner.Payload.Should().BeOfType<RequiredMessage>();
            envelopeSerializer.SizeHint(generatedEnvelope).Should().Be(system.Serialization.Serialize(generatedEnvelope).Length);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Generated serializer should treat NoSender as null-equivalent")]
    public void Generated_serializer_should_treat_NoSender_as_null_equivalent()
    {
        var message = new ReplyMessage("order-1", ActorRefs.NoSender);

        RoundTrip(message).Should().Be(message);
    }

    [Fact(DisplayName = "Generated serializer should round-trip a live IActorRef through Akka serialization")]
    public async Task Generated_serializer_should_round_trip_a_live_IActorRef_through_Akka_serialization()
    {
        var setup = ActorSystemSetup.Create(GeneratedTestSerializer.CreateRegistration().CreateSetup());
        var system = ActorSystem.Create("generated-messagepack-live-ref-spec", setup);
        try
        {
            var actorRef = system.ActorOf(Props.Empty, "live-ref-target");
            var message = new ReplyMessage("order-1", actorRef);

            var serializer = system.Serialization.FindSerializerFor(message);
            var bytes = system.Serialization.Serialize(message);
            var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, message);
            var deserialized = system.Serialization.Deserialize(bytes, serializer.Identifier, manifest)
                .Should().BeOfType<ReplyMessage>().Subject;

            deserialized.ReplyTo.Should().NotBeNull();
            deserialized.ReplyTo.Should().NotBe(ActorRefs.NoSender);
            deserialized.ReplyTo!.Path.ToString().Should().Be(actorRef.Path.ToString());
            deserialized.ReplyTo.Should().Be(actorRef);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact(DisplayName = "Generated serializer should round-trip nested value objects without manifests")]
    public void Generated_serializer_should_round_trip_nested_value_objects_without_manifests()
    {
        var address = new ShippingAddress("1 Main St", "Seattle");
        var message = new ShipmentMessage("order-1", address);

        RoundTrip(message).Should().Be(message);

        Action manifest = () => _serializer.Manifest(address);
        manifest.Should().Throw<ArgumentException>()
            .WithMessage("*Unsupported generated serializer type*");
    }

    [Fact(DisplayName = "Generated serializer should write nested value objects inline")]
    public void Generated_serializer_should_write_nested_value_objects_inline()
    {
        var message = new ShipmentMessage("order-1", new ShippingAddress("1 Main St", "Seattle"));
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("order-1");
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("1 Main St");
        reader.ReadInt32().Should().Be(2);
        reader.ReadString().Should().Be("Seattle");
        reader.Consumed.Should().Be(bytes.Length);
    }

    [Fact(DisplayName = "Generated serializer should round-trip multi-level nested value objects")]
    public void Generated_serializer_should_round_trip_multi_level_nested_value_objects()
    {
        var message = new WarehouseMessage(
            "warehouse-1",
            new WarehouseInfo(
                new WarehouseLocation(
                    "Seattle",
                    new CountryInfo("US"))));

        RoundTrip(message).Should().Be(message);
    }

    [Fact(DisplayName = "Generated serializer should write multi-level nested value objects inline")]
    public void Generated_serializer_should_write_multi_level_nested_value_objects_inline()
    {
        var message = new WarehouseMessage(
            "warehouse-1",
            new WarehouseInfo(
                new WarehouseLocation(
                    "Seattle",
                    new CountryInfo("US"))));
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("warehouse-1");
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("Seattle");
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("US");
        reader.Consumed.Should().Be(bytes.Length);
    }

    private static DateTime ReadDateTime(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        arrayLength.Should().Be(2);

        var ticks = reader.ReadInt64();
        var kind = (DateTimeKind)reader.ReadInt32();
        return new DateTime(ticks, kind);
    }

    private static void WriteDateTime(ref MessagePackWriter writer, DateTime value)
    {
        writer.WriteArrayHeader(2);
        writer.Write(value.Ticks);
        writer.Write((int)value.Kind);
    }

    private static DateTimeOffset ReadDateTimeOffset(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        arrayLength.Should().Be(2);

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

    private static decimal ReadDecimal(ref MessagePackReader reader)
    {
        var arrayLength = reader.ReadArrayHeader();
        arrayLength.Should().Be(4);

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

    private static Guid ReadGuid(ref MessagePackReader reader)
    {
        var bytes = reader.ReadBytes();
        bytes.Should().NotBeNull();
        bytes!.Value.Length.Should().Be(16);

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

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IGeneratedTestProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }

    private static OpaqueSerializedPayload CaptureOpaquePayload(ExtendedActorSystem system, object payload)
    {
        var serializer = system.Serialization.FindSerializerFor(payload);
        var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, payload);
        return new OpaqueSerializedPayload(serializer.Identifier, manifest, serializer.ToBinary(payload));
    }

    private static object RecoverOpaquePayload(ExtendedActorSystem system, OpaqueSerializedPayload payload)
    {
        return system.Serialization.Deserialize(payload.Bytes, payload.SerializerId, payload.Manifest);
    }

    private static TMessage RoundTripThroughSerialization<TMessage>(ActorSystem system, TMessage message)
    {
        var serializer = system.Serialization.FindSerializerFor(message!);
        var bytes = system.Serialization.Serialize(message!);
        var manifest = global::Akka.Serialization.Serialization.ManifestFor(serializer, message!);
        return system.Serialization.Deserialize(bytes, serializer.Identifier, manifest).Should().BeOfType<TMessage>().Subject;
    }

    private static void AssertOpaqueEnvelopeBytes(byte[] envelopeBytes, byte[] expectedInnerBytes)
    {
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(envelopeBytes));
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("envelope-1");
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(3);
        reader.ReadInt32().Should().Be(1);
        reader.ReadInt32().Should().Be(CustomProtobufPayloadSerializer.IdentifierValue);
        reader.ReadInt32().Should().Be(2);
        reader.ReadString().Should().Be(CustomProtobufPayloadSerializer.ManifestName);
        reader.ReadInt32().Should().Be(3);
        var writtenInnerBytes = reader.ReadBytes();
        writtenInnerBytes.Should().NotBeNull();
        writtenInnerBytes!.Value.ToArray().Should().Equal(expectedInnerBytes);
        reader.Consumed.Should().Be(envelopeBytes.Length);
    }
}

public interface IGeneratedTestProtocol
{
}

[AkkaSerializer(Name = "generated-test", SerializerId = 120101)]
public sealed partial class GeneratedTestSerializer : MessagePackSerializer<IGeneratedTestProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

public enum SampleStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}

[AkkaSerializable(Manifest = "primitive-v1")]
public sealed record PrimitiveMessage(
    [property: AkkaField(1)] string Text,
    [property: AkkaField(2)] int IntValue,
    [property: AkkaField(3)] long LongValue,
    [property: AkkaField(4)] bool BooleanValue,
    [property: AkkaField(5)] double DoubleValue,
    [property: AkkaField(6)] decimal DecimalValue,
    [property: AkkaField(7)] Guid GuidValue,
    [property: AkkaField(8)] DateTime Timestamp,
    [property: AkkaField(9)] DateTimeOffset TimestampOffset,
    [property: AkkaField(10)] SampleStatus Status,
    [property: AkkaField(11)] IActorRef? ReplyTo) : IGeneratedTestProtocol;

[AkkaSerializable(Manifest = RequiredMessage.ManifestName)]
public sealed record RequiredMessage(
    [property: AkkaField(1)] string Name,
    [property: AkkaField(2)] int Quantity) : IGeneratedTestProtocol
{
    public const string ManifestName = "required-v1";
}

[AkkaSerializable(Manifest = "opaque-envelope-v1")]
public sealed record OpaqueEnvelope(
    [property: AkkaField(1)] string EnvelopeId,
    [property: AkkaField(2)] OpaqueSerializedPayload Payload) : IGeneratedTestProtocol;

[AkkaSerializable]
public sealed record OpaqueSerializedPayload(
    [property: AkkaField(1)] int SerializerId,
    [property: AkkaField(2)] string Manifest,
    [property: AkkaField(3)] byte[] Bytes);

[AkkaSerializable(Manifest = "attribute-outer-envelope-v1")]
public sealed record AttributeOuterEnvelope(
    [property: AkkaField(1)] string EnvelopeId,
    [property: AkkaField(2), AkkaEnvelopePayload] AttributeInnerEnvelope Inner) : IGeneratedTestProtocol;

[AkkaSerializable(Manifest = "attribute-inner-envelope-v1")]
public sealed record AttributeInnerEnvelope(
    [property: AkkaField(1)] string EnvelopeId,
    [property: AkkaField(2), AkkaEnvelopePayload] object Payload) : IGeneratedTestProtocol;

public sealed record CustomProtobufPayload(string PayloadId, int Value);

public sealed class CustomProtobufPayloadSerializer : global::Akka.Serialization.SerializerWithStringManifest
{
    public const int IdentifierValue = 120202;
    public const string ManifestName = "custom-protobuf-v1";

    public CustomProtobufPayloadSerializer(ExtendedActorSystem system) : base(system)
    {
    }

    public override int Identifier => IdentifierValue;

    public override string Manifest(object o)
    {
        return o switch
        {
            CustomProtobufPayload => ManifestName,
            _ => throw new ArgumentException($"Unsupported custom protobuf serializer type: {o.GetType()}", nameof(o))
        };
    }

    public override byte[] ToBinary(object obj)
    {
        if (obj is not CustomProtobufPayload payload)
            throw new ArgumentException($"Unsupported custom protobuf serializer type: {obj.GetType()}", nameof(obj));

        return Encoding.UTF8.GetBytes($"fake-protobuf|{payload.PayloadId}|{payload.Value.ToString(CultureInfo.InvariantCulture)}");
    }

    public override object FromBinary(byte[] bytes, string manifest)
    {
        if (manifest != ManifestName)
            throw new SerializationException($"Unknown custom protobuf manifest [{manifest}].");

        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 3 || parts[0] != "fake-protobuf")
            throw new SerializationException("Invalid custom protobuf payload bytes.");

        return new CustomProtobufPayload(parts[1], int.Parse(parts[2], CultureInfo.InvariantCulture));
    }
}

[AkkaSerializable(Manifest = OptionalMessage.ManifestName)]
public sealed record OptionalMessage(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2)] int? OptionalInt,
    [property: AkkaField(3)] Guid? OptionalGuid,
    [property: AkkaField(4)] DateTime? OptionalTimestamp,
    [property: AkkaField(5)] SampleStatus? OptionalStatus,
    [property: AkkaField(6)] string? OptionalText,
    [property: AkkaField(7)] ShippingAddress? OptionalAddress) : IGeneratedTestProtocol
{
    public const string ManifestName = "optional-v1";
}

[AkkaSerializable(Manifest = "sparse-v1")]
public sealed record SparseFieldMessage(
    [property: AkkaField(2)] int Quantity,
    [property: AkkaField(10)] string Name) : IGeneratedTestProtocol;

[AkkaSerializable(Manifest = "reply-v1")]
public sealed record ReplyMessage(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] IActorRef? ReplyTo) : IGeneratedTestProtocol;

[AkkaSerializable(Manifest = "shipment-v1")]
public sealed record ShipmentMessage(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] ShippingAddress Address) : IGeneratedTestProtocol;

[AkkaSerializable]
public sealed record ShippingAddress(
    [property: AkkaField(1)] string Street,
    [property: AkkaField(2)] string City);

[AkkaSerializable(Manifest = "warehouse-v1")]
public sealed record WarehouseMessage(
    [property: AkkaField(1)] string WarehouseId,
    [property: AkkaField(2)] WarehouseInfo Warehouse) : IGeneratedTestProtocol;

[AkkaSerializable]
public sealed record WarehouseInfo(
    [property: AkkaField(1)] WarehouseLocation Location);

[AkkaSerializable]
public sealed record WarehouseLocation(
    [property: AkkaField(1)] string City,
    [property: AkkaField(2)] CountryInfo Country);

[AkkaSerializable]
public sealed record CountryInfo(
    [property: AkkaField(1)] string IsoCode);
