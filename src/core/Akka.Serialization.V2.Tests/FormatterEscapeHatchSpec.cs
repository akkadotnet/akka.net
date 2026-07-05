//-----------------------------------------------------------------------
// <copyright file="FormatterEscapeHatchSpec.cs" company="Akka.NET Project">
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
using FluentAssertions;
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Validates the <see cref="AkkaSerializerFormatterAttribute"/> escape hatch: hand-written
/// <see cref="IAkkaMessagePackFormatter{T}"/> registrations for foreign types (core Akka types
/// like <see cref="Address"/> and <see cref="ActorPath"/>) that the generator cannot annotate
/// with <see cref="AkkaSerializableAttribute"/>.
/// </summary>
public sealed class FormatterEscapeHatchSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private ControlMirrorSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("formatter-escape-hatch-spec");
        _serializer = new ControlMirrorSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "Control-mirror serializer should round-trip handshake messages with and without host/port")]
    public void Control_mirror_serializer_should_round_trip_handshake_messages()
    {
        var withHostPort = new Address("akka", "sys", "localhost", 2552);
        var withoutHostPort = new Address("akka", "sys");

        var reqWithHostPortFrom = new MirrorHandshakeReq(new TestUniqueAddress(withHostPort, 17L), withoutHostPort);
        var reqWithoutHostPortFrom = new MirrorHandshakeReq(new TestUniqueAddress(withoutHostPort, 42L), withHostPort);
        var rsp = new MirrorHandshakeRsp(new TestUniqueAddress(withHostPort, 99L));

        RoundTrip(reqWithHostPortFrom).Should().Be(reqWithHostPortFrom);
        RoundTrip(reqWithoutHostPortFrom).Should().Be(reqWithoutHostPortFrom);
        RoundTrip(rsp).Should().Be(rsp);
    }

    [Fact(DisplayName = "Control-mirror serializer should produce Artery-compatible handshake request bytes")]
    public void Control_mirror_serializer_should_produce_artery_compatible_request_bytes()
    {
        var from = new TestUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L);
        var to = new Address("akka", "sys");
        var message = new MirrorHandshakeReq(from, to);

        var actualBytes = _serializer.ToBinary(message);
        var expectedBytes = WriteExpectedReqBytes(from, to);

        actualBytes.Should().Equal(expectedBytes);
    }

    [Fact(DisplayName = "Control-mirror serializer should produce Artery-compatible handshake response bytes")]
    public void Control_mirror_serializer_should_produce_artery_compatible_response_bytes()
    {
        var from = new TestUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L);
        var message = new MirrorHandshakeRsp(from);

        var actualBytes = _serializer.ToBinary(message);
        var expectedBytes = WriteExpectedRspBytes(from);

        actualBytes.Should().Equal(expectedBytes);
    }

    [Fact(DisplayName = "Control-mirror serializer should report exact size hints for handshake messages")]
    public void Control_mirror_serializer_should_report_exact_size_hints_for_handshake_messages()
    {
        var req = new MirrorHandshakeReq(new TestUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L), new Address("akka", "sys"));
        var rsp = new MirrorHandshakeRsp(new TestUniqueAddress(new Address("akka", "sys"), 5L));

        _serializer.SizeHint(req).Should().Be(_serializer.ToBinary(req).Length);
        _serializer.SizeHint(rsp).Should().Be(_serializer.ToBinary(rsp).Length);
    }

    [Fact(DisplayName = "Control-mirror serializer should skip unknown field ids for a formatted message")]
    public void Control_mirror_serializer_should_skip_unknown_field_ids()
    {
        var expectedFrom = new TestUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L);
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(99);
        writer.Write("ignored-future-field");
        writer.Write(1);
        WriteExpectedUniqueAddress(ref writer, expectedFrom);
        writer.Flush();

        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(buffer.WrittenMemory), MirrorHandshakeRsp.ManifestName);

        deserialized.Should().Be(new MirrorHandshakeRsp(expectedFrom));
    }

    [Fact(DisplayName = "Control-mirror serializer should round-trip nullable formatted fields")]
    public void Control_mirror_serializer_should_round_trip_nullable_formatted_fields()
    {
        var withValues = new MirrorNullableMessage(
            new Address("akka", "sys", "localhost", 2552),
            new TestUniqueAddress(new Address("akka", "sys"), 3L));
        var withoutValues = new MirrorNullableMessage(null, null);

        RoundTrip(withValues).Should().Be(withValues);
        RoundTrip(withoutValues).Should().Be(withoutValues);
    }

    [Fact(DisplayName = "Control-mirror serializer should write nil for null formatted fields and keep exact size hints")]
    public void Control_mirror_serializer_should_write_nil_for_null_formatted_fields()
    {
        var message = new MirrorNullableMessage(null, null);
        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));

        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.TryReadNil().Should().BeTrue();
        reader.ReadInt32().Should().Be(2);
        reader.TryReadNil().Should().BeTrue();
        reader.Consumed.Should().Be(bytes.Length);

        _serializer.SizeHint(message).Should().Be(bytes.Length);

        var withValues = new MirrorNullableMessage(
            new Address("akka", "sys", "localhost", 2552),
            new TestUniqueAddress(new Address("akka", "sys"), 3L));
        _serializer.SizeHint(withValues).Should().Be(_serializer.ToBinary(withValues).Length);
    }

    [Fact(DisplayName = "Control-mirror serializer should round-trip ActorPath fields via the built-in ActorPathFormatter")]
    public void Control_mirror_serializer_should_round_trip_actor_path_fields()
    {
        var actorRef = _system.ActorOf(Props.Create<NoopActor>(), "actor-path-target");
        var message = new MirrorActorPathMessage(actorRef.Path);

        var roundTripped = RoundTrip(message);
        roundTripped.Path.ToString().Should().Be(message.Path.ToString());
        _serializer.SizeHint(message).Should().Be(_serializer.ToBinary(message).Length);

        // A path whose address already carries host+port is preserved as-is.
        var remotePath = new MirrorActorPathMessage(ActorPath.Parse("akka://sys@localhost:2552/user/foo"));
        RoundTrip(remotePath).Path.ToString().Should().Be(remotePath.Path.ToString());
    }

    [Fact(DisplayName = "System-constructed ActorPathFormatter should anchor non-transport writes to the system's default address")]
    public void System_constructed_ActorPathFormatter_should_anchor_non_transport_writes_to_the_default_address()
    {
        // The generated serializer prefers ActorPathFormatter's ExtendedActorSystem ctor, so a
        // local path serialized OUTSIDE any transport scope is written with the system's
        // Provider.DefaultAddress (matching Serialization.SerializedActorPath semantics) and
        // stays remotely resolvable.
        var actorRef = _system.ActorOf(Props.Create<NoopActor>(), "default-address-target");
        var message = new MirrorActorPathMessage(actorRef.Path);
        var defaultAddress = ((ExtendedActorSystem)_system).Provider.DefaultAddress;

        var bytes = _serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        var writtenPath = reader.ReadString();

        writtenPath.Should().NotBeNull();
        writtenPath.Should().StartWith(defaultAddress.ToString());
        ActorPath.Parse(writtenPath!).Address.Should().Be(defaultAddress);
        ActorPath.Parse(writtenPath!).ToString().Should().Be(actorRef.Path.ToString());
    }

    [Fact(DisplayName = "Generator should prefer the ExtendedActorSystem constructor when a formatter also has a parameterless one")]
    public async Task Generator_should_prefer_the_ExtendedActorSystem_constructor_when_a_formatter_also_has_a_parameterless_one()
    {
        var system = ActorSystem.Create("system-formatter-spec");
        try
        {
            AssertSystemTaggedFormatterUsesInjectedSystem((ExtendedActorSystem)system);
        }
        finally
        {
            await system.Terminate();
        }
    }

    private static void AssertSystemTaggedFormatterUsesInjectedSystem(ExtendedActorSystem system)
    {
        var serializer = new SystemFormatterSerializer(system);
        var message = new SystemTaggedMessage(new SystemTaggedValue("hello"));

        var bytes = serializer.ToBinary(message);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(1);
        reader.ReadInt32().Should().Be(1);
        // SystemTaggedValueFormatter has BOTH a public parameterless ctor (sentinel tag) and a
        // public (ExtendedActorSystem) ctor: seeing the system name (not the sentinel) proves
        // the system ctor won.
        var tagged = reader.ReadString();
        tagged.Should().Be($"{system.Name}|hello");
        tagged.Should().NotBe($"{SystemTaggedValueFormatter.NoSystemTag}|hello");

        var deserialized = serializer.FromBinary(bytes, SystemTaggedMessage.ManifestName)
            .Should().BeOfType<SystemTaggedMessage>().Subject;
        deserialized.Tagged.Value.Should().Be("hello");
    }

    [Fact(DisplayName = "Generator should honor internal declared accessibility for internal serializers")]
    public void Generator_should_honor_internal_declared_accessibility_for_internal_serializers()
    {
        var serializer = new InternalMirrorSerializer((ExtendedActorSystem)_system);
        var message = new InternalMirrorMessage("payload");

        var bytes = serializer.ToBinary(message);
        var deserialized = serializer.FromBinary(bytes, InternalMirrorMessage.ManifestName);

        deserialized.Should().Be(message);
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IControlMirrorProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }

    private static byte[] WriteExpectedReqBytes(TestUniqueAddress from, Address to)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(1);
        WriteExpectedUniqueAddress(ref writer, from);
        writer.Write(2);
        WriteExpectedAddress(ref writer, to);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static byte[] WriteExpectedRspBytes(TestUniqueAddress from)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(1);
        WriteExpectedUniqueAddress(ref writer, from);
        writer.Flush();
        return buffer.WrittenMemory.ToArray();
    }

    private static void WriteExpectedUniqueAddress(ref MessagePackWriter writer, TestUniqueAddress value)
    {
        writer.WriteArrayHeader(2);
        WriteExpectedAddress(ref writer, value.Address);
        writer.Write(value.Uid);
    }

    private static void WriteExpectedAddress(ref MessagePackWriter writer, Address address)
    {
        writer.WriteArrayHeader(4);
        writer.Write(address.Protocol);
        writer.Write(address.System);

        if (address.Host is { } host)
            writer.Write(host);
        else
            writer.WriteNil();

        if (address.Port is { } port)
            writer.Write(port);
        else
            writer.WriteNil();
    }
}

/// <summary>
/// Minimal no-op actor used only so tests can obtain a live, resolvable <see cref="IActorRef"/> / <see cref="ActorPath"/>.
/// </summary>
public sealed class NoopActor : ReceiveActor
{
}

public interface IControlMirrorProtocol
{
}

/// <summary>
/// Mirrors <c>Akka.Remote.Artery.UniqueAddress</c>-shaped payload data for byte-compatibility testing
/// without referencing Akka.Remote.
/// </summary>
public readonly record struct TestUniqueAddress(Address Address, long Uid);

/// <summary>
/// Hand-written formatter for <see cref="TestUniqueAddress"/>, composing <see cref="AddressFormatter"/>
/// internally. Wire format is byte-identical to Akka.Remote's hand-rolled
/// <c>ArteryControlMessageSerializer.WriteUniqueAddress</c>/<c>ReadUniqueAddress</c>/<c>SizeOfUniqueAddress</c>.
/// </summary>
public sealed class TestUniqueAddressFormatter : IAkkaMessagePackFormatter<TestUniqueAddress>
{
    private readonly AddressFormatter _addressFormatter = new();

    public void Write(ref MessagePackWriter writer, TestUniqueAddress value)
    {
        writer.WriteArrayHeader(2);
        _addressFormatter.Write(ref writer, value.Address);
        writer.Write(value.Uid);
    }

    public TestUniqueAddress Read(ref MessagePackReader reader)
    {
        var length = reader.ReadArrayHeader();
        if (length != 2)
            throw new SerializationException($"Expected a 2-element unique-address array, got {length}.");

        var address = _addressFormatter.Read(ref reader);
        var uid = reader.ReadInt64();
        return new TestUniqueAddress(address, uid);
    }

    public int SizeOf(TestUniqueAddress value)
    {
        var addressSize = _addressFormatter.SizeOf(value.Address);
        if (addressSize < 0)
            return global::Akka.Serialization.SerializerV2.UnknownSize;

        return MessagePackSizes.SizeOfArrayHeader(2) + addressSize + MessagePackSizes.SizeOfInt64(value.Uid);
    }
}

[AkkaSerializable(Manifest = MirrorHandshakeReq.ManifestName)]
public sealed record MirrorHandshakeReq(
    [property: AkkaField(1)] TestUniqueAddress From,
    [property: AkkaField(2)] Address To) : IControlMirrorProtocol
{
    public const string ManifestName = "HSReq";
}

[AkkaSerializable(Manifest = MirrorHandshakeRsp.ManifestName)]
public sealed record MirrorHandshakeRsp(
    [property: AkkaField(1)] TestUniqueAddress From) : IControlMirrorProtocol
{
    public const string ManifestName = "HSRsp";
}

[AkkaSerializable(Manifest = MirrorNullableMessage.ManifestName)]
public sealed record MirrorNullableMessage(
    [property: AkkaField(1)] Address? OptionalAddress,
    [property: AkkaField(2)] TestUniqueAddress? OptionalUniqueAddress) : IControlMirrorProtocol
{
    public const string ManifestName = "mirror-nullable-v1";
}

[AkkaSerializable(Manifest = MirrorActorPathMessage.ManifestName)]
public sealed record MirrorActorPathMessage(
    [property: AkkaField(1)] ActorPath Path) : IControlMirrorProtocol
{
    public const string ManifestName = "mirror-actor-path-v1";
}

[AkkaSerializer(Name = "control-mirror", SerializerId = 120102)]
[AkkaSerializerFormatter(typeof(Address), typeof(AddressFormatter))]
[AkkaSerializerFormatter(typeof(TestUniqueAddress), typeof(TestUniqueAddressFormatter))]
[AkkaSerializerFormatter(typeof(ActorPath), typeof(ActorPathFormatter))]
public sealed partial class ControlMirrorSerializer : MessagePackSerializer<IControlMirrorProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

public interface ISystemFormatterProtocol
{
}

public sealed record SystemTaggedValue(string Value);

/// <summary>
/// Test-only formatter that proves the generator PREFERS the <see cref="ExtendedActorSystem"/>
/// constructor overload when both it and a public parameterless constructor exist: it tags the
/// written value with the owning system's name (or a sentinel when constructed without a system)
/// so the test can assert the real system instance was injected.
/// </summary>
public sealed class SystemTaggedValueFormatter : IAkkaMessagePackFormatter<SystemTaggedValue>
{
    public const string NoSystemTag = "no-system";

    private readonly string _tag;

    public SystemTaggedValueFormatter()
    {
        _tag = NoSystemTag;
    }

    public SystemTaggedValueFormatter(ExtendedActorSystem system)
    {
        _tag = system.Name;
    }

    public void Write(ref MessagePackWriter writer, SystemTaggedValue value)
    {
        writer.Write($"{_tag}|{value.Value}");
    }

    public SystemTaggedValue Read(ref MessagePackReader reader)
    {
        var raw = reader.ReadString() ?? throw new SerializationException("Missing tagged value.");
        var separatorIndex = raw.IndexOf('|');
        return new SystemTaggedValue(separatorIndex >= 0 ? raw.Substring(separatorIndex + 1) : raw);
    }

    public int SizeOf(SystemTaggedValue value) => global::Akka.Serialization.SerializerV2.UnknownSize;
}

[AkkaSerializable(Manifest = SystemTaggedMessage.ManifestName)]
public sealed record SystemTaggedMessage(
    [property: AkkaField(1)] SystemTaggedValue Tagged) : ISystemFormatterProtocol
{
    public const string ManifestName = "system-tagged-v1";
}

[AkkaSerializer(Name = "system-formatter", SerializerId = 120103)]
[AkkaSerializerFormatter(typeof(SystemTaggedValue), typeof(SystemTaggedValueFormatter))]
public sealed partial class SystemFormatterSerializer : MessagePackSerializer<ISystemFormatterProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}

internal interface IInternalMirrorProtocol
{
}

[AkkaSerializable(Manifest = InternalMirrorMessage.ManifestName)]
internal sealed record InternalMirrorMessage(
    [property: AkkaField(1)] string Value) : IInternalMirrorProtocol
{
    public const string ManifestName = "internal-mirror-v1";
}

[AkkaSerializer(Name = "internal-mirror", SerializerId = 120104)]
internal sealed partial class InternalMirrorSerializer : MessagePackSerializer<IInternalMirrorProtocol>
{
    public static partial SerializerRegistration CreateRegistration();
}
