//-----------------------------------------------------------------------
// <copyright file="FieldlessAndStructFieldSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Covers two source-generator gaps found while attempting to swap Artery's control messages
/// (handshake, heartbeat) onto generated serializers:
/// <list type="bullet">
/// <item>
/// A deliberately fieldless top-level protocol message ("arrival IS the signal" -- Artery's
/// <c>ArteryHeartbeat</c>/<c>ArteryHeartbeatRsp</c>) used to be hard-rejected by AKKASG004 with no
/// opt-in. <see cref="AkkaSerializableAttribute.AllowEmpty"/> lifts that for messages that opt in,
/// while keeping AKKASG004 as a guardrail for everyone else (a forgotten <c>[AkkaField]</c> is far
/// more likely than a genuinely empty message).
/// </item>
/// <item>
/// An <c>[AkkaSerializable]</c> value type (a <c>readonly record struct</c>, mirroring Artery's
/// <c>UniqueAddress</c>) used as a required nested field used to generate an <c>Inner?</c>-vs-<c>Inner</c>
/// mismatch (CS1503) because the generator treated every <see cref="FieldKind"/>-equivalent
/// "Object" field as reference-like unconditionally.
/// </item>
/// </list>
/// </summary>
public sealed class FieldlessAndStructFieldSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private GapFixSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("fieldless-and-struct-field-spec");
        _serializer = new GapFixSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "AllowEmpty fieldless messages should round-trip and write a bare empty map header")]
    public void AllowEmpty_fieldless_messages_should_round_trip_and_write_a_bare_empty_map_header()
    {
        var heartbeat = new GapHeartbeat();
        var heartbeatRsp = new GapHeartbeatRsp();

        RoundTrip(heartbeat).Should().Be(heartbeat);
        RoundTrip(heartbeatRsp).Should().Be(heartbeatRsp);

        var bytes = _serializer.ToBinary(heartbeat);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(0);
        reader.Consumed.Should().Be(bytes.Length);
        bytes.Should().HaveCount(1, because: "an empty MessagePack map header is a single byte (0x80)");
    }

    [Fact(DisplayName = "AllowEmpty fieldless messages should report exact size hints")]
    public void AllowEmpty_fieldless_messages_should_report_exact_size_hints()
    {
        var heartbeat = new GapHeartbeat();

        _serializer.SizeHint(heartbeat).Should().Be(_serializer.ToBinary(heartbeat).Length);
    }

    [Fact(DisplayName = "AllowEmpty fieldless messages should tolerate a forward-compatible map with unknown fields")]
    public void AllowEmpty_fieldless_messages_should_tolerate_unknown_fields()
    {
        // A future sender version adds fields to what is, on this reader, still a fieldless
        // message. The skip-loop read must tolerate them rather than choke on a non-zero map header.
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(1);
        writer.Write("future-field-value");
        writer.Write(99);
        writer.WriteArrayHeader(2);
        writer.Write("nested");
        writer.Write(7);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), GapHeartbeat.ManifestName);

        deserialized.Should().Be(new GapHeartbeat());
    }

    [Fact(DisplayName = "Fieldless message without AllowEmpty behavior is unaffected: existing non-empty messages still round-trip")]
    public void Existing_non_empty_messages_should_still_round_trip()
    {
        var message = new GapPlainMessage("hello");

        RoundTrip(message).Should().Be(message);
    }

    [Fact(DisplayName = "Required [AkkaSerializable] struct nested field should compile and round-trip")]
    public void Required_struct_nested_field_should_round_trip()
    {
        var withHostPort = new GapUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L);
        var withoutHostPort = new GapUniqueAddress(new Address("akka", "sys"), 42L);

        RoundTrip(new GapHandshakeMessage(withHostPort)).Should().Be(new GapHandshakeMessage(withHostPort));
        RoundTrip(new GapHandshakeMessage(withoutHostPort)).Should().Be(new GapHandshakeMessage(withoutHostPort));
    }

    [Fact(DisplayName = "Required [AkkaSerializable] struct nested field should write inline without a nil wrapper")]
    public void Required_struct_nested_field_should_write_inline()
    {
        var from = new GapUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L);
        var message = new GapHandshakeMessage(from);

        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        // GapUniqueAddress is [AkkaSerializable] like any other nested message: it writes its own
        // explicit field-id map (2 fields: Address at index 1, Uid at index 2), NOT an array --
        // only the AddressFormatter escape hatch composed inside it uses an array convention.
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadArrayHeader().Should().Be(4); // Address (via AddressFormatter): [protocol, system, host, port]
        reader.ReadString().Should().Be("akka");
        reader.ReadString().Should().Be("sys");
        reader.ReadString().Should().Be("localhost");
        reader.ReadInt32().Should().Be(2552);
        reader.ReadInt32().Should().Be(2);
        reader.ReadInt64().Should().Be(17L);
        reader.Consumed.Should().Be(bytes.Length);
    }

    [Fact(DisplayName = "Required [AkkaSerializable] struct nested field should report exact size hints")]
    public void Required_struct_nested_field_should_report_exact_size_hints()
    {
        var message = new GapHandshakeMessage(new GapUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L));

        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);
    }

    [Fact(DisplayName = "Optional [AkkaSerializable] struct nested field should round-trip with and without a value")]
    public void Optional_struct_nested_field_should_round_trip()
    {
        var withValue = new GapOptionalHandshakeMessage(new GapUniqueAddress(new Address("akka", "sys"), 3L));
        var withoutValue = new GapOptionalHandshakeMessage(null);

        RoundTrip(withValue).Should().Be(withValue);
        RoundTrip(withoutValue).Should().Be(withoutValue);
    }

    [Fact(DisplayName = "Optional [AkkaSerializable] struct nested field should write nil when absent and keep exact size hints")]
    public void Optional_struct_nested_field_should_write_nil_when_absent()
    {
        var message = new GapOptionalHandshakeMessage(null);
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        reader.TryReadNil().Should().BeTrue();
        reader.Consumed.Should().Be(bytes.Length);

        _serializer.SizeHint(message).Should().Be(bytes.Length);

        var withValue = new GapOptionalHandshakeMessage(new GapUniqueAddress(new Address("akka", "sys"), 3L));
        _serializer.SizeHint(withValue).Should().Be(_serializer.ToBinary(withValue).Length);
    }

    [Fact(DisplayName = "Optional [AkkaSerializable] struct nested field should tolerate missing entries on read")]
    public void Optional_struct_nested_field_should_allow_missing_field_on_read()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(0);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), GapOptionalHandshakeMessage.ManifestName);

        deserialized.Should().Be(new GapOptionalHandshakeMessage(null));
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IGapFixProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface IGapFixProtocol
{
}

/// <summary>
/// Mirrors Artery's <c>ArteryHeartbeat</c>: a deliberately fieldless message where arrival IS the
/// signal, with nothing to carry.
/// </summary>
[AkkaSerializable(Manifest = GapHeartbeat.ManifestName, AllowEmpty = true)]
public sealed record GapHeartbeat : IGapFixProtocol
{
    public const string ManifestName = "gap-heartbeat-v1";
}

/// <summary>
/// Mirrors Artery's <c>ArteryHeartbeatRsp</c>: also deliberately fieldless.
/// </summary>
[AkkaSerializable(Manifest = GapHeartbeatRsp.ManifestName, AllowEmpty = true)]
public sealed record GapHeartbeatRsp : IGapFixProtocol
{
    public const string ManifestName = "gap-heartbeat-rsp-v1";
}

[AkkaSerializable(Manifest = "gap-plain-v1")]
public sealed record GapPlainMessage(
    [property: AkkaField(1)] string Value) : IGapFixProtocol;

/// <summary>
/// Mirrors Akka.Remote.Artery's <c>UniqueAddress</c> shape, but handled directly by the
/// generator's native nested-Object path (annotated with <see cref="AkkaSerializableAttribute"/>)
/// instead of the <see cref="AkkaSerializerFormatterAttribute"/> foreign-type escape hatch.
/// </summary>
[AkkaSerializable]
public readonly record struct GapUniqueAddress(
    [property: AkkaField(1)] Address Address,
    [property: AkkaField(2)] long Uid);

[AkkaSerializable(Manifest = GapHandshakeMessage.ManifestName)]
public sealed record GapHandshakeMessage(
    [property: AkkaField(1)] GapUniqueAddress From) : IGapFixProtocol
{
    public const string ManifestName = "gap-handshake-v1";
}

[AkkaSerializable(Manifest = GapOptionalHandshakeMessage.ManifestName)]
public sealed record GapOptionalHandshakeMessage(
    [property: AkkaField(1)] GapUniqueAddress? From) : IGapFixProtocol
{
    public const string ManifestName = "gap-optional-handshake-v1";
}

[AkkaSerializer(Name = "gap-fix", SerializerId = 120801)]
[AkkaSerializerFormatter(typeof(Address), typeof(AddressFormatter))]
public sealed partial class GapFixSerializer : MessagePackSerializer<IGapFixProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}
