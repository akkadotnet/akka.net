//-----------------------------------------------------------------------
// <copyright file="GeneratedClosedGenericExpansionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Serialization.V2;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Specs for Decision 18 (openspec/changes/messagepack-sourcegen-validation/design.md, "Closed-Set
/// Expansion And Adoption On The Serializer"): a registration whose type argument has a closed set
/// (the serializer's own protocol interface, or a type-level <c>[AkkaUnion]</c>) expands to one
/// closed construction per member, with a manifest derived by a fixed formula; a registration on
/// the serializer adopts any <c>[AkkaSerializable]</c> type, generic or not, whether or not it
/// implements the protocol; and a field typed as the protocol interface is a union over the
/// protocol set with no <c>[AkkaUnion]</c> needed. <see cref="ExpansionSerializer"/> mirrors the
/// customer shape from the design record: <see cref="ExpansionEnvelope{T}"/> plays the role of the
/// customer's own generic envelope, registered once over the protocol interface.
/// </summary>
public sealed class GeneratedClosedGenericExpansionSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private ExpansionSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("closed-generic-expansion-spec");
        _serializer = new ExpansionSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "ManifestPrefix expansion should derive one manifest per protocol member as prefix + '/' + the member's own manifest")]
    public void Expansion_should_derive_one_manifest_per_protocol_member()
    {
        var cancelledEnvelope = new ExpansionEnvelope<ExpansionOrderCancelled>(new ExpansionOrderCancelled("order-1"), "trace-1");

        // ExpansionOrderCancelled's own manifest is "order-cancelled-v1"; with no explicit override
        // registered for this construction, the derived formula applies: "env" + "/" + "order-cancelled-v1".
        _serializer.Manifest(cancelledEnvelope).Should().Be("env/order-cancelled-v1");
        RoundTrip(cancelledEnvelope).Should().Be(cancelledEnvelope);
    }

    [Fact(DisplayName = "An explicit registration for one expanded construction should override its derived manifest")]
    public void Explicit_registration_should_override_derived_manifest_for_one_construction()
    {
        var cassetteEnvelope = new ExpansionEnvelope<ExpansionAcceptCassette>(new ExpansionAcceptCassette(5), "trace-2");

        // Envelope<ExpansionAcceptCassette> is ALSO explicitly registered with its own Manifest
        // ("env-cassette-v1"), which wins over the derived "env/accept-cassette-v1" -- Decision 18's
        // explicit-override rule.
        _serializer.Manifest(cassetteEnvelope).Should().Be("env-cassette-v1");
        RoundTrip(cassetteEnvelope).Should().Be(cassetteEnvelope);
    }

    [Fact(DisplayName = "The literal construction's protocol-interface field should be encoded as a union over the protocol set, with no [AkkaUnion] attribute")]
    public void Literal_construction_field_should_be_an_implicit_protocol_union()
    {
        object cassetteBody = new ExpansionAcceptCassette(9);
        var literalEnvelope = new ExpansionEnvelope<IExpansionProtocol>((IExpansionProtocol)cassetteBody, "trace-3");

        // Manifest = "env-any" was set alongside ManifestPrefix, so the literal construction --
        // Envelope<IExpansionProtocol> itself -- is ALSO registered, distinct from every expanded
        // member.
        _serializer.Manifest(literalEnvelope).Should().Be("env-any");

        var result = RoundTrip(literalEnvelope);
        result.TraceId.Should().Be("trace-3");
        result.Message.Should().BeOfType<ExpansionAcceptCassette>();
        result.Message.Should().Be(cassetteBody);
    }

    [Fact(DisplayName = "A multi-argument registration should expand to the product of its arguments' sets, resolving a fixed nested-construction argument through its own sibling registration")]
    public void Multi_argument_registration_should_expand_to_the_product_of_its_arguments_sets()
    {
        // ExpansionPair<IExpansionProtocol, ExpansionEnvelope<ExpansionAcceptCassette>> expands only
        // over its FIRST argument (the protocol set); its second argument is a FIXED nested
        // construction ("G<H<M>>" in the design's own formula), whose manifest can only come from
        // its own separate registration above ("env-cassette-v1") -- a generic definition's own
        // Manifest is always ignored (AKKASG037).
        var fixedSecond = new ExpansionEnvelope<ExpansionAcceptCassette>(new ExpansionAcceptCassette(1), "trace-4");
        var pairWithCassette = new ExpansionPair<ExpansionAcceptCassette, ExpansionEnvelope<ExpansionAcceptCassette>>(new ExpansionAcceptCassette(2), fixedSecond);
        var pairWithCancelled = new ExpansionPair<ExpansionOrderCancelled, ExpansionEnvelope<ExpansionAcceptCassette>>(new ExpansionOrderCancelled("order-2"), fixedSecond);

        _serializer.Manifest(pairWithCassette).Should().Be("pair/accept-cassette-v1/env-cassette-v1");
        _serializer.Manifest(pairWithCancelled).Should().Be("pair/order-cancelled-v1/env-cassette-v1");

        RoundTrip(pairWithCassette).Should().Be(pairWithCassette);
        RoundTrip(pairWithCancelled).Should().Be(pairWithCancelled);
    }

    [Fact(DisplayName = "A registration should adopt a non-generic, non-protocol type, and its own Manifest should override the type's own attribute")]
    public void Registration_should_adopt_a_non_generic_non_protocol_type()
    {
        var auditStamp = new ExpansionAuditStamp("/user/some-actor");

        // ExpansionAuditStamp implements no protocol at all; its own [AkkaSerializable] attribute
        // declares Manifest = "audit-v1", but the registration's own Manifest overrides it.
        _serializer.Manifest(auditStamp).Should().Be("audit-override-v1");

        var bytes = _serializer.ToBinary(auditStamp);
        var result = _serializer.FromBinary(bytes, _serializer.Manifest(auditStamp));
        result.Should().Be(auditStamp);
    }

    [Fact(DisplayName = "A construction built by reflection over a type outside the registered set should fail at send time")]
    public void Reflection_built_construction_outside_the_set_should_fail_at_send_time()
    {
        // The customer shape from the design record: Envelope<T> constructed via
        // Activator.CreateInstance/MakeGenericType, over a type this generator never saw at build
        // time. A construction the generator cannot see does not exist -- it fails at send time
        // naming the unsupported runtime type, rather than being silently accepted or dropped.
        var curveball = new ExpansionCurveball("not in the set");
        var envelopeType = typeof(ExpansionEnvelope<>).MakeGenericType(typeof(ExpansionCurveball));
        var runtimeEnvelope = Activator.CreateInstance(envelopeType, curveball, "trace-5")!;

        var act = () => _serializer.Manifest(runtimeEnvelope);

        act.Should().Throw<ArgumentException>().WithMessage("*Unsupported generated serializer type*");
    }

    [Fact(DisplayName = "Reflection over the customer's own closed-set member should still dispatch, because the generator saw that exact construction at build time")]
    public void Reflection_built_construction_inside_the_set_should_round_trip()
    {
        // Same reflection-based construction pattern as the previous test, but over a type that IS
        // in the protocol set: Envelope<AcceptCassette> is one of the constructions this generator's
        // expansion produced, so its EXACT runtime type still matches the generated dispatch arm.
        var cassette = new ExpansionAcceptCassette(3);
        var envelopeType = typeof(ExpansionEnvelope<>).MakeGenericType(typeof(ExpansionAcceptCassette));
        var runtimeEnvelope = Activator.CreateInstance(envelopeType, cassette, "trace-6")!;

        _serializer.Manifest(runtimeEnvelope).Should().Be("env-cassette-v1");

        var bytes = _serializer.ToBinary(runtimeEnvelope);
        var result = _serializer.FromBinary(bytes, _serializer.Manifest(runtimeEnvelope));
        result.Should().Be(runtimeEnvelope);
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : notnull
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }
}

public interface IExpansionProtocol
{
}

[AkkaSerializable(Manifest = "accept-cassette-v1")]
public sealed record ExpansionAcceptCassette([property: AkkaField(1)] int Layer) : IExpansionProtocol;

[AkkaSerializable(Manifest = "order-cancelled-v1")]
public sealed record ExpansionOrderCancelled([property: AkkaField(1)] string OrderId) : IExpansionProtocol;

/// <summary>
/// Plays the role of the customer's own generic envelope (design record, Decision 18): a plain
/// generic wrapper that cannot implement <see cref="IExpansionProtocol"/> in the customer's real
/// shape (it lives in a different assembly than the protocol's implementors), so it is deliberately
/// left without a protocol declaration here too, even though nothing stops it compiling in the same
/// assembly for this test.
/// </summary>
[AkkaSerializable]
public sealed record ExpansionEnvelope<T>(
    [property: AkkaField(1)] T Message,
    [property: AkkaField(2)] string TraceId);

[AkkaSerializable]
public sealed record ExpansionPair<TFirst, TSecond>(
    [property: AkkaField(1)] TFirst First,
    [property: AkkaField(2)] TSecond Second);

[AkkaSerializable(Manifest = "audit-v1")]
public sealed record ExpansionAuditStamp([property: AkkaField(1)] string ActorPath);

/// <summary>A type never registered on <see cref="ExpansionSerializer"/> and outside its protocol set -- deliberately unreachable, for the reflection-outside-the-set test.</summary>
[AkkaSerializable(Manifest = "curveball-v1")]
public sealed record ExpansionCurveball([property: AkkaField(1)] string Note);

[AkkaSerializer<IExpansionProtocol>("closed-generic-expansion-test", 140301)]
[AkkaSerializable<ExpansionEnvelope<IExpansionProtocol>>(ManifestPrefix = "env", Manifest = "env-any")]
[AkkaSerializable<ExpansionEnvelope<ExpansionAcceptCassette>>(Manifest = "env-cassette-v1")]
[AkkaSerializable<ExpansionPair<IExpansionProtocol, ExpansionEnvelope<ExpansionAcceptCassette>>>(ManifestPrefix = "pair")]
[AkkaSerializable<ExpansionAuditStamp>(Manifest = "audit-override-v1")]
public sealed partial class ExpansionSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}
