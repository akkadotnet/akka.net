//-----------------------------------------------------------------------
// <copyright file="GeneratedUnionSpec.cs" company="Akka.NET Project">
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
/// Specs for <c>[AkkaUnion]</c> fields: closed, explicitly-enumerated member sets encoded
/// structurally inline and discriminated by each member's serializer-owned manifest -- the typed
/// alternative to <c>[AkkaEnvelopePayload]</c> for payload sets known at compile time.
/// </summary>
public sealed class GeneratedUnionSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private UnionTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("generated-union-spec");
        _serializer = new UnionTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "Union field should round-trip a class member as its concrete type")]
    public void Union_field_should_round_trip_class_member()
    {
        var message = new UnionEnvelope("env-1", new OrderPlaced("order-1", 3));

        var result = RoundTrip(message);

        result.Should().Be(message);
        result.Event.Should().BeOfType<OrderPlaced>();
    }

    [Fact(DisplayName = "Union field should round-trip a nested-only member whose manifest exists purely as the union discriminator")]
    public void Union_field_should_round_trip_nested_only_member()
    {
        // OrderCancelled does NOT implement IUnionTestProtocol: it is never a top-level message,
        // so its manifest exists solely to discriminate it inside the union.
        var message = new UnionEnvelope("env-2", new OrderCancelled("order-2", "customer request"));

        var result = RoundTrip(message);

        result.Should().Be(message);
        result.Event.Should().BeOfType<OrderCancelled>();
    }

    [Fact(DisplayName = "Union field should round-trip a struct member through boxing")]
    public void Union_field_should_round_trip_struct_member()
    {
        var message = new UnionEnvelope("env-3", new OrderNote("order-3", "expedite"));

        var result = RoundTrip(message);

        result.Should().Be(message);
        result.Event.Should().BeOfType<OrderNote>();
    }

    [Fact(DisplayName = "Union wire format should be a manifest-discriminated 2-entry map with inline member fields")]
    public void Union_wire_format_should_be_manifest_discriminated_map()
    {
        var message = new UnionEnvelope("env-4", new OrderPlaced("order-4", 7));
        var bytes = _serializer.ToBinary(message);

        var reader = new MessagePackReader(new ReadOnlySequence<byte>(bytes));
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("env-4");
        reader.ReadInt32().Should().Be(2);

        // The union frame: { 1: manifest, 2: inline member field map }. No serializer id, no
        // length-prefixed opaque byte blob -- contrast with the [AkkaEnvelopePayload] frame.
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be(OrderPlaced.ManifestName);
        reader.ReadInt32().Should().Be(2);
        reader.ReadMapHeader().Should().Be(2);
        reader.ReadInt32().Should().Be(1);
        reader.ReadString().Should().Be("order-4");
        reader.ReadInt32().Should().Be(2);
        reader.ReadInt32().Should().Be(7);
        reader.Consumed.Should().Be(bytes.Length);
    }

    [Fact(DisplayName = "Union SizeHint should be exact for every member kind")]
    public void Union_size_hint_should_be_exact()
    {
        var classMember = new UnionEnvelope("env-5", new OrderPlaced("order-5", 11));
        var structMember = new UnionEnvelope("env-6", new OrderNote("order-6", "gift wrap"));

        _serializer.SizeHint(classMember).Should().Be(_serializer.ToBinary(classMember).Length);
        _serializer.SizeHint(structMember).Should().Be(_serializer.ToBinary(structMember).Length);
    }

    [Fact(DisplayName = "Nullable union field should round-trip null and non-null values")]
    public void Nullable_union_field_should_round_trip_null_and_values()
    {
        var withValue = new OptionalUnionMessage("id-1", new OrderCancelled("order-7", "oops"));
        var withNull = new OptionalUnionMessage("id-2", null);

        RoundTrip(withValue).Should().Be(withValue);
        RoundTrip(withNull).Should().Be(withNull);
    }

    [Fact(DisplayName = "Field-level union override should narrow the type-level member set")]
    public void Field_level_union_override_should_narrow_type_level_set()
    {
        // OrderNote is a member of the TYPE-LEVEL union on IOrderEvent, but OptionalUnionMessage
        // overrides the field with a narrower set that excludes it: the override must win.
        var message = new OptionalUnionMessage("id-3", new OrderNote("order-12", "excluded"));

        var write = () => _serializer.ToBinary(message);

        write.Should().Throw<SerializationException>()
            .WithMessage("*not a declared union member*");
    }

    [Fact(DisplayName = "Union write should fail serialization for an undeclared runtime type")]
    public void Union_write_should_fail_for_undeclared_runtime_type()
    {
        var message = new UnionEnvelope("env-7", new UndeclaredEvent());

        var write = () => _serializer.ToBinary(message);

        write.Should().Throw<SerializationException>()
            .WithMessage("*not a declared union member*");
    }

    [Fact(DisplayName = "Union write should fail serialization for an undeclared subtype of a declared member")]
    public void Union_write_should_fail_for_undeclared_subtype_of_declared_member()
    {
        // Exact-runtime-type dispatch: a subtype of a declared member must NOT silently serialize
        // as its base (it would drop the subtype's state on the wire).
        var message = new UnionEnvelope("env-8", new DerivedOrderCancelled("order-8", "reason", "extra"));

        var write = () => _serializer.ToBinary(message);

        write.Should().Throw<SerializationException>()
            .WithMessage("*not a declared union member*");
    }

    [Fact(DisplayName = "Union SizeHint should report UnknownSize for an undeclared runtime type")]
    public void Union_size_hint_should_report_unknown_for_undeclared_type()
    {
        var message = new UnionEnvelope("env-9", new UndeclaredEvent());

        _serializer.SizeHint(message).Should().Be(global::Akka.Serialization.SerializerV2.UnknownSize);
    }

    [Fact(DisplayName = "Union read should fail on an unknown manifest")]
    public void Union_read_should_fail_on_unknown_manifest()
    {
        var bytes = BuildUnionEnvelopeBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(2);
            writer.Write(1);
            writer.Write("no-such-manifest-v1");
            writer.Write(2);
            writer.WriteMapHeader(0);
        });

        var read = () => _serializer.FromBinary(bytes, "union-envelope-v1");

        read.Should().Throw<SerializationException>()
            .WithMessage("*Unknown union manifest*no-such-manifest-v1*");
    }

    [Fact(DisplayName = "Union read should fail when the payload precedes the manifest")]
    public void Union_read_should_fail_when_payload_precedes_manifest()
    {
        var bytes = BuildUnionEnvelopeBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(2);
            writer.Write(2);
            writer.WriteMapHeader(0);
            writer.Write(1);
            writer.Write(OrderPlaced.ManifestName);
        });

        var read = () => _serializer.FromBinary(bytes, "union-envelope-v1");

        read.Should().Throw<SerializationException>()
            .WithMessage("*manifest must precede*");
    }

    [Fact(DisplayName = "Union read should fail when the payload is missing entirely")]
    public void Union_read_should_fail_when_payload_missing()
    {
        var bytes = BuildUnionEnvelopeBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(1);
            writer.Write(1);
            writer.Write(OrderPlaced.ManifestName);
        });

        var read = () => _serializer.FromBinary(bytes, "union-envelope-v1");

        read.Should().Throw<SerializationException>()
            .WithMessage("*Missing union payload*");
    }

    [Fact(DisplayName = "Union read should skip unknown keys in the union frame for forward compatibility")]
    public void Union_read_should_skip_unknown_union_frame_keys()
    {
        var bytes = BuildUnionEnvelopeBytes((ref MessagePackWriter writer) =>
        {
            writer.WriteMapHeader(3);
            writer.Write(1);
            writer.Write(OrderPlaced.ManifestName);
            writer.Write(2);
            writer.WriteMapHeader(2);
            writer.Write(1);
            writer.Write("order-10");
            writer.Write(2);
            writer.Write(10);
            writer.Write(99);
            writer.Write("future-union-metadata");
        });

        var result = _serializer.FromBinary(bytes, "union-envelope-v1")
            .Should().BeOfType<UnionEnvelope>().Subject;

        result.Event.Should().Be(new OrderPlaced("order-10", 10));
    }

    [Fact(DisplayName = "Union member that is also a top-level message should keep one manifest across both roles")]
    public void Union_member_should_reuse_manifest_across_roles()
    {
        // OrderPlaced is both a top-level protocol message and a union member. The SAME manifest
        // drives ordinary serializer dispatch and union dispatch -- one identity, two paths.
        var topLevel = new OrderPlaced("order-11", 2);

        _serializer.Manifest(topLevel).Should().Be(OrderPlaced.ManifestName);
        var roundTripped = _serializer.FromBinary(_serializer.ToBinary(topLevel), OrderPlaced.ManifestName);
        roundTripped.Should().Be(topLevel);
    }

    /// <summary>
    /// Builds raw <see cref="UnionEnvelope"/> bytes with a caller-controlled union frame so read-side
    /// failure modes (unknown manifest, ordering, missing payload) can be exercised directly.
    /// </summary>
    private static byte[] BuildUnionEnvelopeBytes(WriteUnionFrame writeUnionFrame)
    {
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);
        writer.Write(1);
        writer.Write("crafted");
        writer.Write(2);
        writeUnionFrame(ref writer);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private delegate void WriteUnionFrame(ref MessagePackWriter writer);

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IUnionTestProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface IUnionTestProtocol
{
}

/// <summary>
/// The union's static field type, carrying the TYPE-LEVEL member declaration: stated once here,
/// inherited by every field of this type (mirroring [JsonDerivedType] and the case list of the
/// proposed C# language unions). Individual fields may override with their own [AkkaUnion].
/// </summary>
[AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled), typeof(OrderNote))]
public interface IOrderEvent
{
}

[AkkaSerializer<IUnionTestProtocol>(Name = "union-test", SerializerId = 120303)]
public sealed partial class UnionTestSerializer : MessagePackSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}

/// <summary>Inherits the full member set from the type-level union on <see cref="IOrderEvent"/>.</summary>
[AkkaSerializable(Manifest = "union-envelope-v1")]
public sealed record UnionEnvelope(
    [property: AkkaField(1)] string EnvelopeId,
    [property: AkkaField(2)] IOrderEvent Event) : IUnionTestProtocol;

/// <summary>
/// Field-level OVERRIDE: narrows the type-level member set to exclude <see cref="OrderNote"/> for
/// this one field only.
/// </summary>
[AkkaSerializable(Manifest = "optional-union-v1")]
public sealed record OptionalUnionMessage(
    [property: AkkaField(1)] string Id,
    [property: AkkaField(2), AkkaUnion(typeof(OrderPlaced), typeof(OrderCancelled))]
    IOrderEvent? MaybeEvent) : IUnionTestProtocol;

/// <summary>A union member that is ALSO a top-level protocol message: one manifest, two roles.</summary>
[AkkaSerializable(Manifest = ManifestName)]
public sealed record OrderPlaced(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] int Quantity) : IOrderEvent, IUnionTestProtocol
{
    public const string ManifestName = "order-placed-v1";
}

/// <summary>A nested-only union member: its manifest exists purely as the union discriminator.</summary>
[AkkaSerializable(Manifest = "order-cancelled-v1")]
public record OrderCancelled(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Reason) : IOrderEvent;

/// <summary>A value-type union member: dispatched by exact boxed runtime type.</summary>
[AkkaSerializable(Manifest = "order-note-v1")]
public readonly record struct OrderNote(
    [property: AkkaField(1)] string OrderId,
    [property: AkkaField(2)] string Note) : IOrderEvent;

/// <summary>Implements the field's static type but is NOT a declared union member.</summary>
public sealed record UndeclaredEvent : IOrderEvent;

/// <summary>An undeclared SUBTYPE of a declared member: must fail, not truncate to its base.</summary>
public sealed record DerivedOrderCancelled(string OrderId, string Reason, string Extra)
    : OrderCancelled(OrderId, Reason);
