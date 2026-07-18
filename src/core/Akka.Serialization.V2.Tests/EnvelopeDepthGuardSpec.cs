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
using Akka.Configuration;
using Akka.TestKit;
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
public sealed class EnvelopeDepthGuardSpec : AkkaSpec
{
    // Comfortably past the internal limit (100) so the test never straddles the exact boundary.
    private const int OverLimitDepth = 250;
    private const int GeneratedTestSerializerId = 120101;
    private const string InnerEnvelopeManifest = "attribute-inner-envelope-v1";

    // Register the source-generated serializer the way a real application would: via the classic HOCON
    // akka.actor.serializers / serialization-bindings blocks (mirrors ClassicRemotingSpec in this project
    // and Akka.Tests' CustomSerializerSpec), binding the IGeneratedTestProtocol marker interface -- which
    // AttributeInnerEnvelope and RequiredMessage both implement -- to the GeneratedTestSerializer. The
    // generated serializer exposes a fixed Identifier (120101) and an (ExtendedActorSystem) constructor,
    // so HOCON reflection-based registration resolves it exactly like the setup-based registration did.
    private static readonly Config SerializerConfig = ConfigurationFactory.ParseString(@"
        akka.actor {
            serializers {
                generated-test = ""Akka.Serialization.V2.Tests.GeneratedTestSerializer, Akka.Serialization.V2.Tests""
            }
            serialization-bindings {
                ""Akka.Serialization.V2.Tests.IGeneratedTestProtocol, Akka.Serialization.V2.Tests"" = generated-test
            }
        }");

    public EnvelopeDepthGuardSpec(ITestOutputHelper output)
        : base(SerializerConfig, output)
    {
    }

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

        var bytes = Sys.Serialization.Serialize(shallow);
        var recovered = Sys.Serialization.Deserialize(bytes, GeneratedTestSerializerId, InnerEnvelopeManifest);

        recovered.Should().Be(shallow);
    }

    [Fact(DisplayName = "Serializing a self-nesting envelope past the depth limit throws instead of overflowing the stack")]
    public void Deep_envelope_chain_throws_on_write()
    {
        var deep = BuildChain(OverLimitDepth);

        // The point of the guard: this returns (throws) rather than crashing the process with an
        // uncatchable StackOverflowException. A test that merely completes proves the process survived.
        Action serialize = () => Sys.Serialization.Serialize(deep);

        serialize.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }

    [Fact(DisplayName = "SizeHint on a self-nesting envelope past the depth limit throws instead of overflowing the stack")]
    public void Deep_envelope_chain_throws_on_size()
    {
        var deep = BuildChain(OverLimitDepth);
        var serializer = Sys.Serialization.FindSerializerFor(deep)
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
        var innerBytes = Sys.Serialization.Serialize(atLimit);

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
            Sys.Serialization.Deserialize(overLimitBytes, GeneratedTestSerializerId, InnerEnvelopeManifest);

        deserialize.Should().Throw<SerializationException>().WithMessage("*maximum depth*");
    }

    [Fact(DisplayName = "The depth counter unwinds to zero after an over-limit failure so later serializations on the same thread still succeed (no thread poisoning)")]
    public void Depth_counter_unwinds_after_failure_leaving_thread_usable()
    {
        // The guard tracks nesting in a [ThreadStatic] counter guarded by try/finally. A regression that
        // left the counter unbalanced after an exception (enter without a matching exit) would poison
        // every subsequent serialization on the same thread. This test runs synchronously, so both steps
        // below execute on the same thread -- exactly the surface such a leak would corrupt.

        // 1. Trip the guard on the write path and confirm it throws (not crashes).
        var deep = BuildChain(OverLimitDepth);
        Action overLimit = () => Sys.Serialization.Serialize(deep);
        overLimit.Should().Throw<SerializationException>().WithMessage("*maximum depth*");

        // 2. Immediately, on the same thread, a normal shallow message must still serialize AND round-trip.
        //    Success proves the counter unwound back to zero rather than staying elevated from the failure.
        var shallow = BuildChain(5);
        var bytes = Sys.Serialization.Serialize(shallow);
        var recovered = Sys.Serialization.Deserialize(bytes, GeneratedTestSerializerId, InnerEnvelopeManifest);

        recovered.Should().Be(shallow);
    }
}
