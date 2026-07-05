//-----------------------------------------------------------------------
// <copyright file="ManifestContractSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Pins the <c>SerializerV2.Manifest</c> invariant documented in serializer-v2 design.md
/// Decision 14 for generated serializers: manifests are cheap, stable, derivable without
/// serializing, non-empty, non-CLR, and bounded by Artery's envelope literal u16 length prefix
/// (65,535 UTF-8 bytes). Covers every top-level message type of both
/// <see cref="GeneratedTestSerializer"/> and <see cref="ControlMirrorSerializer"/>.
/// </summary>
public sealed class ManifestContractSpec : IAsyncLifetime
{
    private const int ArteryManifestLiteralLimit = 65535;

    private ActorSystem _system = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("manifest-contract-spec");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    public static IEnumerable<object> GeneratedTestProtocolMessages()
    {
        yield return new PrimitiveMessage(
            "text", 1, 2L, true, 3.0d, 4m,
            Guid.Parse("8f7d35c8-2931-4a48-9b84-2c008ab7f2e4"),
            new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero),
            SampleStatus.Pending,
            ActorRefs.NoSender);
        yield return new RequiredMessage("m", 1);
        yield return new OpaqueEnvelope("e", new OpaqueSerializedPayload(1, "m", Array.Empty<byte>()));
        yield return new AttributeOuterEnvelope("o", new AttributeInnerEnvelope("i", new RequiredMessage("m", 1)));
        yield return new AttributeInnerEnvelope("i", new RequiredMessage("m", 1));
        yield return new OptionalMessage("o", null, null, null, null, null, null);
        yield return new SparseFieldMessage(1, "n");
        yield return new ReplyMessage("c", null);
        yield return new ShipmentMessage("o", new ShippingAddress("1 Main St", "Seattle"));
        yield return new WarehouseMessage("w", new WarehouseInfo(new WarehouseLocation("Seattle", new CountryInfo("US"))));
    }

    public static IEnumerable<object> ControlMirrorProtocolMessages()
    {
        yield return new MirrorHandshakeReq(new TestUniqueAddress(new Address("akka", "sys", "localhost", 2552), 17L), new Address("akka", "sys"));
        yield return new MirrorHandshakeRsp(new TestUniqueAddress(new Address("akka", "sys"), 42L));
        yield return new MirrorNullableMessage(null, null);
        yield return new MirrorActorPathMessage(ActorPath.Parse("akka://sys/user/foo"));
    }

    [Fact(DisplayName = "Manifest should be derivable from a fresh serializer instance without serializing")]
    public void Manifest_should_be_derivable_from_a_fresh_serializer_instance_without_serializing()
    {
        // Fresh instances with nothing serialized yet: Manifest must not require a prior or
        // accompanying Serialize call.
        AssertForEachMessage((serializer, message) =>
        {
            var manifest = serializer.Manifest(message);
            manifest.Should().NotBeNullOrWhiteSpace(because: $"a fresh serializer must produce a manifest for [{message.GetType().Name}] before anything is serialized");
        });
    }

    [Fact(DisplayName = "Manifest should be stable across repeated calls and fresh serializer instances")]
    public void Manifest_should_be_stable_across_repeated_calls_and_fresh_serializer_instances()
    {
        var extendedSystem = (ExtendedActorSystem)_system;
        AssertForEachSerializer((createSerializer, messages) =>
        {
            var first = createSerializer(extendedSystem);
            var second = createSerializer(extendedSystem);
            foreach (var message in messages)
            {
                var manifest = first.Manifest(message);
                first.Manifest(message).Should().Be(manifest, because: "repeated calls on the same instance must return equal manifests");
                second.Manifest(message).Should().Be(manifest, because: "manifests are wire contracts and must not vary per serializer instance");
            }
        });
    }

    [Fact(DisplayName = "Manifest should be non-empty and never a CLR type name")]
    public void Manifest_should_be_non_empty_and_never_a_CLR_type_name()
    {
        AssertForEachMessage((serializer, message) =>
        {
            var manifest = serializer.Manifest(message);
            manifest.Should().NotBeNullOrEmpty();
            manifest.Should().NotBe(message.GetType().FullName, because: "manifests must be serializer-owned tokens, not CLR type names");
            manifest.Should().NotBe(message.GetType().AssemblyQualifiedName, because: "manifests must be serializer-owned tokens, not assembly-qualified type names");
        });
    }

    [Fact(DisplayName = "Manifest should fit within Artery's envelope literal u16 length limit")]
    public void Manifest_should_fit_within_Artery_envelope_literal_u16_length_limit()
    {
        AssertForEachMessage((serializer, message) =>
        {
            var manifest = serializer.Manifest(message);
            Encoding.UTF8.GetByteCount(manifest).Should().BeLessOrEqualTo(
                ArteryManifestLiteralLimit,
                because: "Artery envelope manifest literals carry a u16 length prefix");
        });
    }

    private void AssertForEachMessage(Action<global::Akka.Serialization.SerializerV2, object> assertion)
    {
        var extendedSystem = (ExtendedActorSystem)_system;
        AssertForEachSerializer((createSerializer, messages) =>
        {
            var serializer = createSerializer(extendedSystem);
            foreach (var message in messages)
                assertion(serializer, message);
        });
    }

    private static void AssertForEachSerializer(
        Action<Func<ExtendedActorSystem, global::Akka.Serialization.SerializerV2>, IEnumerable<object>> assertion)
    {
        assertion(system => new GeneratedTestSerializer(system), GeneratedTestProtocolMessages());
        assertion(system => new ControlMirrorSerializer(system), ControlMirrorProtocolMessages());
    }
}
