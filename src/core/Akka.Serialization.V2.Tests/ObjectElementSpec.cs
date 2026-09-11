//-----------------------------------------------------------------------
// <copyright file="ObjectElementSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Serialization.V2.Tests.Harness;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Decision 20 (a property typed <c>object</c> is always the envelope-payload boundary) extended to
/// collection elements: a <c>List&lt;object&gt;</c>, <c>object[]</c>, or any other natively-supported
/// collection whose element type is <c>object</c> (or <c>object?</c>) treats each element as its own
/// envelope-payload boundary -- the same frame a property typed <c>object</c> already gets. Fixture
/// message types (<see cref="ObjectListMessage"/>, <see cref="ObjectArrayMessage"/>,
/// <see cref="ObjectListNullableMessage"/>, <see cref="ObjectDictValuesMessage"/>,
/// <see cref="ObjectImmutableListMessage"/>) live alongside the rest of the shared
/// <see cref="IGeneratedTestProtocol"/> fixture in GeneratedMessagePackSerializerSpec.cs.
/// </summary>
public sealed class ObjectElementSpec : IAsyncLifetime
{
    private ActorSystem _system = null!;
    private GeneratedTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        // A real ActorSystem, not a bare `new GeneratedTestSerializer(...)`, because each object
        // element's WriteEnvelopePayload/ReadEnvelopePayload/SizeOfEnvelopePayload call resolves its
        // OWN serializer through `system.Serialization.FindSerializerFor` -- exercising that lookup
        // (rather than bypassing it) is the point of these tests. Binds IGeneratedTestProtocol to
        // GeneratedTestSerializer (so a RequiredMessage/AttributeInnerEnvelope element round-trips
        // through the SAME generated serializer as the outer message) and CustomProtobufPayload to a
        // non-SerializerV2 serializer (so an element's SizeHint can be deliberately un-sizable).
        var setup = ActorSystemSetup.Create(SerializationSetup.Create(extendedSystem =>
        {
            var generated = GeneratedTestSerializer.CreateRegistration().CreateDetails(extendedSystem);
            var custom = SerializerDetails.Create(
                "custom-protobuf",
                new CustomProtobufPayloadSerializer(extendedSystem),
                ImmutableHashSet.Create<Type>(typeof(CustomProtobufPayload)));
            return ImmutableHashSet.Create(generated, custom);
        }));
        _system = ActorSystem.Create("object-element-spec", setup);
        _serializer = new GeneratedTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    private TMessage RoundTrip<TMessage>(TMessage message)
        where TMessage : class, IGeneratedTestProtocol
    {
        var bytes = _serializer.ToBinary(message);
        var manifest = _serializer.Manifest(message);
        return _serializer.FromBinary(bytes, manifest).Should().BeOfType<TMessage>().Subject;
    }

    [Fact(DisplayName = "List<object> round-trips a mix of a message from the same serializer, a plain string, and a boxed int")]
    public void Should_round_trip_List_of_object_with_mixed_payload_types()
    {
        var message = new ObjectListMessage(new List<object>
        {
            new RequiredMessage("order-1", 42),
            "plain-string",
            7
        });

        var result = RoundTrip(message);

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().Be(new RequiredMessage("order-1", 42));
        result.Items[1].Should().Be("plain-string");
        result.Items[2].Should().Be(7);
    }

    [Fact(DisplayName = "object[] round-trips a mix of a message from the same serializer, a plain string, and a boxed int")]
    public void Should_round_trip_object_array_with_mixed_payload_types()
    {
        var message = new ObjectArrayMessage(new object[]
        {
            new RequiredMessage("order-2", 5),
            "another-string",
            99
        });

        var result = RoundTrip(message);

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().Be(new RequiredMessage("order-2", 5));
        result.Items[1].Should().Be("another-string");
        result.Items[2].Should().Be(99);
    }

    [Fact(DisplayName = "List<object?> round-trips a null element alongside real payloads")]
    public void Should_round_trip_nullable_object_element_with_null()
    {
        var message = new ObjectListNullableMessage(new List<object?>
        {
            new RequiredMessage("order-3", 1),
            null,
            "text"
        });

        var result = RoundTrip(message);

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().Be(new RequiredMessage("order-3", 1));
        result.Items[1].Should().BeNull();
        result.Items[2].Should().Be("text");
    }

    [Fact(DisplayName = "Dictionary<string, object> values round-trip as envelope payloads")]
    public void Should_round_trip_dictionary_values_as_envelope_payloads()
    {
        var message = new ObjectDictValuesMessage(new Dictionary<string, object>
        {
            ["a"] = new RequiredMessage("order-4", 3),
            ["b"] = "value-b",
            ["c"] = 123
        });

        var result = RoundTrip(message);

        result.Values.Should().HaveCount(3);
        result.Values["a"].Should().Be(new RequiredMessage("order-4", 3));
        result.Values["b"].Should().Be("value-b");
        result.Values["c"].Should().Be(123);
    }

    [Fact(DisplayName = "ImmutableList<object> round-trips a mix of payload types")]
    public void Should_round_trip_immutable_list_of_object()
    {
        var message = new ObjectImmutableListMessage(ImmutableList.Create<object>(
            new RequiredMessage("order-5", 9),
            "immutable-text",
            17));

        var result = RoundTrip(message);

        result.Items.Should().HaveCount(3);
        result.Items[0].Should().Be(new RequiredMessage("order-5", 9));
        result.Items[1].Should().Be("immutable-text");
        result.Items[2].Should().Be(17);
    }

    [Fact(DisplayName = "SizeHint returns UnknownSize when a List<object> element's own serializer cannot size it")]
    public void SizeHint_should_return_UnknownSize_when_element_serializer_cannot_size()
    {
        // CustomProtobufPayloadSerializer is a SerializerWithStringManifest, not a SerializerV2, so
        // SizeOfEnvelopePayload gives up on it (UnknownSize) exactly as it would for a hand-written,
        // non-generated serializer anywhere else in this codebase -- the propagation guard added to
        // EmitSizeElement's FieldKind.EnvelopePayload case must bubble that all the way out.
        var message = new ObjectListMessage(new List<object>
        {
            "sizable-string",
            new CustomProtobufPayload("payload-1", 17)
        });

        _serializer.SizeHint(message).Should().Be(SerializerV2.UnknownSize);
    }

    [Fact(DisplayName = "An object element holding a self-nesting envelope chain still respects the envelope depth guard")]
    public void List_of_object_element_should_respect_envelope_depth_guard()
    {
        // Comfortably past AkkaSerializer's internal MaxEnvelopePayloadDepth (100) -- see
        // EnvelopeDepthGuardSpec, whose field-level version of this same scenario this mirrors. A
        // collection element's WriteEnvelopePayload call enters/exits the SAME [ThreadStatic] depth
        // counter as a field's, so nesting the chain one level deeper -- inside a List<object>
        // element instead of directly in a property -- must trip the identical guard.
        object payload = new RequiredMessage("leaf", 1);
        for (var i = 0; i < 250; i++)
            payload = new AttributeInnerEnvelope($"lvl-{i}", payload);

        var message = new ObjectListMessage(new List<object> { payload });

        Action serialize = () => _serializer.ToBinary(message);

        serialize.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }

    [Fact(DisplayName = "Generator should reject a Dictionary<object, string> key with AKKASG003")]
    public void Generator_should_reject_object_typed_dictionary_key()
    {
        // A dictionary KEY typed `object` is rejected rather than treated as an envelope-payload
        // boundary: Dictionary<TKey,TValue> throws on a null key at runtime (a nullable `object?` key
        // would crash the moment an envelope legitimately decodes to null), and ReadEnvelopePayload
        // always hands back a brand-new instance on read, so a round-tripped key has no stable
        // identity for hash/equality-based lookups to rediscover. See MapCollectionElement's remarks.
        const string source = """
            #nullable enable
            using System.Collections.Generic;
            using Akka.Actor;
            using Akka.Serialization.V2;

            namespace ObjectKeySample;

            public interface IProtocol
            {
            }

            [AkkaSerializer<IProtocol>("sample", 121101)]
            public sealed partial class SampleSerializer : AkkaSerializer
            {
                public static partial SerializerRegistration CreateRegistration();
            }

            [AkkaSerializable(Manifest = "bad-key-v1")]
            public sealed record BadKeyMessage([property: AkkaField(1)] Dictionary<object, string> Values) : IProtocol;
            """;

        var diagnostics = GeneratorTestHarness.Run(source).AllDiagnostics;

        diagnostics.Should().Contain(diagnostic => diagnostic.Id == "AKKASG003" && diagnostic.Severity == DiagnosticSeverity.Error);
    }
}
