//-----------------------------------------------------------------------
// <copyright file="GeneratedMessagePackSerializerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using FluentAssertions;
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

    [Fact(DisplayName = "AkkaWriter and AkkaReader should round-trip supported built-in field types")]
    public void AkkaWriter_and_AkkaReader_should_round_trip_supported_built_in_field_types()
    {
        var id = Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4");
        var timestamp = new DateTime(2026, 6, 3, 4, 45, 0, DateTimeKind.Utc);
        var timestampOffset = new DateTimeOffset(2026, 6, 3, 4, 45, 0, TimeSpan.FromHours(2));
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new AkkaWriter(buffer);

        writer.BeginObject(9);
        writer.WriteInt32(1);
        writer.WriteString("alpha");
        writer.WriteInt32(2);
        writer.WriteInt32(42);
        writer.WriteInt32(3);
        writer.WriteInt64(9000000000L);
        writer.WriteInt32(4);
        writer.WriteBoolean(true);
        writer.WriteInt32(5);
        writer.WriteDouble(12.5d);
        writer.WriteInt32(6);
        writer.WriteDecimal(123.456m);
        writer.WriteInt32(7);
        writer.WriteGuid(id);
        writer.WriteInt32(8);
        writer.WriteDateTime(timestamp);
        writer.WriteInt32(9);
        writer.WriteDateTimeOffset(timestampOffset);

        var reader = new AkkaReader(new ReadOnlySequence<byte>(buffer.WrittenMemory));
        reader.BeginReadObject().Should().Be(9);
        reader.ReadFieldId().Should().Be(1);
        reader.ReadString().Should().Be("alpha");
        reader.ReadFieldId().Should().Be(2);
        reader.ReadInt32().Should().Be(42);
        reader.ReadFieldId().Should().Be(3);
        reader.ReadInt64().Should().Be(9000000000L);
        reader.ReadFieldId().Should().Be(4);
        reader.ReadBoolean().Should().BeTrue();
        reader.ReadFieldId().Should().Be(5);
        reader.ReadDouble().Should().Be(12.5d);
        reader.ReadFieldId().Should().Be(6);
        reader.ReadDecimal().Should().Be(123.456m);
        reader.ReadFieldId().Should().Be(7);
        reader.ReadGuid().Should().Be(id);
        reader.ReadFieldId().Should().Be(8);
        reader.ReadDateTime().Should().Be(timestamp);
        reader.ReadFieldId().Should().Be(9);
        reader.ReadDateTimeOffset().Should().Be(timestampOffset);
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
        var reader = new AkkaReader(new ReadOnlySequence<byte>(bytes));

        reader.BeginReadObject().Should().Be(2);
        reader.ReadFieldId().Should().Be(2);
        reader.ReadInt32().Should().Be(17);
        reader.ReadFieldId().Should().Be(10);
        reader.ReadString().Should().Be("alpha");
    }

    [Fact(DisplayName = "Generated serializer should skip unknown field IDs")]
    public void Generated_serializer_should_skip_unknown_field_ids()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new AkkaWriter(buffer);
        writer.BeginObject(3);
        writer.WriteInt32(99);
        writer.WriteString("ignored");
        writer.WriteInt32(1);
        writer.WriteString("order-1");
        writer.WriteInt32(2);
        writer.WriteInt32(42);

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), RequiredMessage.ManifestName);

        deserialized.Should().Be(new RequiredMessage("order-1", 42));
    }

    [Fact(DisplayName = "Generated serializer should reject missing non-nullable required fields")]
    public void Generated_serializer_should_reject_missing_non_nullable_required_fields()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new AkkaWriter(buffer);
        writer.BeginObject(1);
        writer.WriteInt32(2);
        writer.WriteInt32(42);

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

    [Fact(DisplayName = "Generated serializer should treat NoSender as null-equivalent")]
    public void Generated_serializer_should_treat_NoSender_as_null_equivalent()
    {
        var message = new ReplyMessage("order-1", ActorRefs.NoSender);

        RoundTrip(message).Should().Be(message);
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IGeneratedTestProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
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

[AkkaSerializable(Manifest = "sparse-v1")]
public sealed record SparseFieldMessage(
    [property: AkkaField(2)] int Quantity,
    [property: AkkaField(10)] string Name) : IGeneratedTestProtocol;

[AkkaSerializable(Manifest = "reply-v1")]
public sealed record ReplyMessage(
    [property: AkkaField(1)] string CorrelationId,
    [property: AkkaField(2)] IActorRef? ReplyTo) : IGeneratedTestProtocol;
