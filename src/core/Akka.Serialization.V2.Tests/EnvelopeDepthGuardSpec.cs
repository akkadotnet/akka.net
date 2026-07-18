//-----------------------------------------------------------------------
// <copyright file="EnvelopeDepthGuardSpec.cs" company="Akka.NET Project">
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
using MessagePack;
using Xunit;

namespace Akka.Serialization.V2.Tests;

/// <summary>
/// Guards the <see cref="AkkaEnvelopePayloadAttribute"/> depth limit. Envelope payloads legitimately
/// nest a level or two, but a message type that declares itself (directly or transitively) as its own
/// envelope payload recurses without bound. Before the guard that recursion overflowed the thread stack
/// and killed the process (an uncatchable .NET failure); the guard turns it into an ordinary catchable
/// <see cref="SerializationException"/> on the write, size, and read paths alike. Reuses the
/// <see cref="AttributeInnerEnvelope"/> test type from the sibling generated-serializer spec, whose
/// <c>[AkkaEnvelopePayload] object Payload</c> field can hold another envelope.
/// </summary>
public sealed class EnvelopeDepthGuardSpec : IAsyncLifetime
{
    // Comfortably past the internal limit (100) so the test never straddles the exact boundary.
    private const int OverLimitDepth = 250;
    private const int GeneratedTestSerializerId = 120101;
    private const string InnerEnvelopeManifest = "attribute-inner-envelope-v1";

    private ActorSystem _system = null!;

    public ValueTask InitializeAsync()
    {
        var setup = ActorSystemSetup.Create(GeneratedTestSerializer.CreateRegistration().CreateSetup());
        _system = ActorSystem.Create("envelope-depth-guard-spec", setup);
        return default;
    }

    public async ValueTask DisposeAsync() => await _system.Terminate();

    private static AttributeInnerEnvelope BuildChain(int depth)
    {
        // Terminal is a plain leaf (no envelope field); each wrapper nests the previous via its payload.
        object payload = new RequiredMessage("leaf", 1);
        for (var i = 0; i < depth; i++)
            payload = new AttributeInnerEnvelope($"lvl-{i}", payload);
        return (AttributeInnerEnvelope)payload;
    }

    [Fact(DisplayName = "A shallow envelope chain still round-trips (the guard does not disturb normal nesting)")]
    public void Shallow_envelope_chain_round_trips()
    {
        var shallow = BuildChain(5);

        var bytes = _system.Serialization.Serialize(shallow);
        var recovered = _system.Serialization.Deserialize(bytes, GeneratedTestSerializerId, InnerEnvelopeManifest);

        recovered.Should().Be(shallow);
    }

    [Fact(DisplayName = "Serializing a self-nesting envelope past the depth limit throws instead of overflowing the stack")]
    public void Deep_envelope_chain_throws_on_write()
    {
        var deep = BuildChain(OverLimitDepth);

        // The point of the guard: this returns (throws) rather than crashing the process with an
        // uncatchable StackOverflowException. A test that merely completes proves the process survived.
        Action serialize = () => _system.Serialization.Serialize(deep);

        serialize.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }

    [Fact(DisplayName = "SizeHint on a self-nesting envelope past the depth limit throws instead of overflowing the stack")]
    public void Deep_envelope_chain_throws_on_size()
    {
        var deep = BuildChain(OverLimitDepth);
        var serializer = _system.Serialization.FindSerializerFor(deep)
            .Should().BeAssignableTo<SerializerV2>().Subject;

        Action size = () => serializer.SizeHint(deep);

        size.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }

    [Fact(DisplayName = "Deserializing a self-nesting envelope past the depth limit throws instead of overflowing the stack")]
    public void Deep_envelope_chain_throws_on_read()
    {
        // A depth-100 chain is writable (exactly at the limit); serialize it to obtain valid inner bytes,
        // then hand-wrap one more envelope level so decoding trips the limit on the way down.
        var atLimit = BuildChain(100);
        var innerBytes = _system.Serialization.Serialize(atLimit);

        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(2);            // AttributeInnerEnvelope body: { 1: EnvelopeId, 2: Payload }
        writer.Write(1);
        writer.Write("over-limit-top");
        writer.Write(2);
        writer.WriteMapHeader(3);            // envelope payload: { 1: serializerId, 2: manifest, 3: bytes }
        writer.Write(1);
        writer.Write(GeneratedTestSerializerId);
        writer.Write(2);
        writer.Write(InnerEnvelopeManifest);
        writer.Write(3);
        writer.Write(innerBytes);
        writer.Flush();
        var overLimitBytes = buffer.WrittenMemory.ToArray();

        Action deserialize = () =>
            _system.Serialization.Deserialize(overLimitBytes, GeneratedTestSerializerId, InnerEnvelopeManifest);

        deserialize.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }
}
