//-----------------------------------------------------------------------
// <copyright file="OversizedPayloadDeterminismSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using FluentAssertions;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Pins deterministic oversized-payload failure for generated serializers (messagepack-sourcegen
/// task 6.8): a caller-imposed cap (<see cref="global::Akka.Serialization.PooledPayloadWriter"/>
/// with <c>maxCapacity</c>, the transport maximum-frame-size hook) makes an oversized payload fail
/// AT ENCODE TIME with a typed <see cref="global::Akka.Serialization.PayloadSizeExceededException"/>
/// carrying the attempted size and the cap — never a truncated or corrupt frame observed
/// downstream. Writer mechanics themselves are covered by <c>Akka.Tests.Serialization.PooledPayloadWriterSpec</c>;
/// this spec pins the SERIALIZER-side contract: the exception surfaces from <c>Serialize</c>, the
/// generated serializer stays stateless across the failure, and the writer is reusable after
/// <see cref="global::Akka.Serialization.PooledPayloadWriter.Reset"/>.
/// </summary>
public sealed class OversizedPayloadDeterminismSpec : IAsyncLifetime
{
    private const int MaxCapacity = 32;

    private ActorSystem _system = null!;
    private GeneratedTestSerializer _serializer = null!;

    public ValueTask InitializeAsync()
    {
        _system = ActorSystem.Create("oversized-payload-determinism-spec");
        _serializer = new GeneratedTestSerializer((ExtendedActorSystem)_system);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _system.Terminate();
    }

    [Fact(DisplayName = "Generated serializer should throw PayloadSizeExceededException at encode time when the payload exceeds the writer cap")]
    public void Generated_serializer_should_throw_PayloadSizeExceededException_when_payload_exceeds_writer_cap()
    {
        var oversized = new RequiredMessage(new string('x', 100), 42);
        using var writer = new global::Akka.Serialization.PooledPayloadWriter(initialCapacityHint: 16, maxCapacity: MaxCapacity);

        Action serialize = () => _serializer.Serialize(oversized, writer);

        var exception = serialize.Should().Throw<global::Akka.Serialization.PayloadSizeExceededException>().Which;
        exception.MaxCapacity.Should().Be(MaxCapacity);
        exception.AttemptedSize.Should().BeGreaterThan(MaxCapacity);
    }

    [Fact(DisplayName = "Generated serializer should remain usable after an oversized-payload failure")]
    public void Generated_serializer_should_remain_usable_after_an_oversized_payload_failure()
    {
        var oversized = new RequiredMessage(new string('x', 100), 42);
        using (var cappedWriter = new global::Akka.Serialization.PooledPayloadWriter(initialCapacityHint: 16, maxCapacity: MaxCapacity))
        {
            Action serialize = () => _serializer.Serialize(oversized, cappedWriter);
            serialize.Should().Throw<global::Akka.Serialization.PayloadSizeExceededException>();
        }

        // Generated serializers are stateless: the SAME instance must round-trip a small message
        // through a fresh writer after the mid-write failure.
        var small = new RequiredMessage("a", 1);
        var bytes = _serializer.ToBinary(small);
        var manifest = _serializer.Manifest(small);

        _serializer.FromBinary(bytes, manifest).Should().Be(small);
    }

    [Fact(DisplayName = "Capped writer should be reusable via Reset after an oversized-payload failure")]
    public void Capped_writer_should_be_reusable_via_Reset_after_an_oversized_payload_failure()
    {
        var oversized = new RequiredMessage(new string('x', 100), 42);
        var small = new RequiredMessage("a", 1);
        using var writer = new global::Akka.Serialization.PooledPayloadWriter(initialCapacityHint: 16, maxCapacity: MaxCapacity);

        Action serialize = () => _serializer.Serialize(oversized, writer);
        serialize.Should().Throw<global::Akka.Serialization.PayloadSizeExceededException>();

        // The transport dead-letter-then-reuse pattern: Reset the SAME writer, encode the next
        // (smaller) message into it, and decode it back from the written bytes.
        writer.Reset();
        var bytesWritten = _serializer.Serialize(small, writer);

        bytesWritten.Should().Be(writer.WrittenCount);
        var deserialized = _serializer.Deserialize(new ReadOnlySequence<byte>(writer.WrittenMemory), _serializer.Manifest(small));
        deserialized.Should().Be(small);
    }

    [Fact(DisplayName = "Capped writer should never exceed its cap when an oversized payload fails mid-encode")]
    public void Capped_writer_should_never_exceed_its_cap_when_an_oversized_payload_fails_mid_encode()
    {
        var oversized = new RequiredMessage(new string('x', 100), 42);
        using var writer = new global::Akka.Serialization.PooledPayloadWriter(initialCapacityHint: 16, maxCapacity: MaxCapacity);

        Action serialize = () => _serializer.Serialize(oversized, writer);
        serialize.Should().Throw<global::Akka.Serialization.PayloadSizeExceededException>();

        // The cap is a hard encode-time boundary: no oversized partial frame ever exists to hand
        // to a transport.
        writer.WrittenCount.Should().BeLessOrEqualTo(MaxCapacity);
    }

    [Fact(DisplayName = "Envelope-payload serialization should throw PayloadSizeExceededException when the staged payload exceeds the outer writer cap")]
    public async Task Envelope_payload_serialization_should_throw_when_staged_payload_exceeds_outer_writer_cap()
    {
        var setup = ActorSystemSetup.Create(GeneratedTestSerializer.CreateRegistration().CreateSetup());
        var system = ActorSystem.Create("oversized-envelope-payload-spec", setup);
        try
        {
            var payload = new RequiredMessage(new string('x', 100), 42);
            var envelope = new AttributeOuterEnvelope("outer-1", new AttributeInnerEnvelope("inner-1", payload));
            var serializer = system.Serialization.FindSerializerFor(envelope)
                .Should().BeAssignableTo<global::Akka.Serialization.SerializerV2>().Subject;

            using var writer = new global::Akka.Serialization.PooledPayloadWriter(initialCapacityHint: 16, maxCapacity: 64);
            Action serialize = () => serializer.Serialize(envelope, writer);

            var exception = serialize.Should().Throw<global::Akka.Serialization.PayloadSizeExceededException>().Which;
            exception.MaxCapacity.Should().Be(64);
            exception.AttemptedSize.Should().BeGreaterThan(64);
        }
        finally
        {
            await system.Terminate();
        }
    }
}
