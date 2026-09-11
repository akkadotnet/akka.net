//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryMessagePackSerializerSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Cluster.Configuration;
using Akka.Cluster.Serialization;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Event;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using FluentAssertions;
using Xunit;

namespace Akka.Cluster.Tests.Serialization;

/// <summary>
/// Parity contract for the MessagePack (Akka.Serialization.V2) fork of the ReliableDelivery
/// serializer: every message type + manifest the legacy protobuf <see cref="ReliableDeliverySerializer"/>
/// (id 36) handles must round-trip identically through the new
/// <see cref="ReliableDeliveryMessagePackSerializer"/> (id 76), the legacy serializer must keep
/// round-tripping everything (no regression), writes must stay bound to the legacy serializer,
/// and both serializers must be resolvable by id from one ActorSystem (dual registration).
/// </summary>
public class ReliableDeliveryMessagePackSerializerSpecs : AkkaSpec
{
    public ReliableDeliveryMessagePackSerializerSpecs(ITestOutputHelper outputHelper) : base(
        ClusterConfigFactory.Default(), outputHelper)
    {
        MsgPackSerializer = new ReliableDeliveryMessagePackSerializer((ExtendedActorSystem)Sys);
        LegacySerializer = new ReliableDeliverySerializer((ExtendedActorSystem)Sys);
        RealActorRef = Sys.ActorOf(BlackHoleActor.Props, "blackhole");
    }

    private ReliableDeliveryMessagePackSerializer MsgPackSerializer { get; }

    private ReliableDeliverySerializer LegacySerializer { get; }

    public static long Timestamp { get; } = DateTime.UtcNow.Ticks;

    public IActorRef RealActorRef { get; }

    /// <summary>
    /// A user-defined payload with no special serializer registration - rides through the
    /// delivery envelopes via the system's fallback (JSON) serializer, exercising the opaque
    /// (serializerId, manifest, bytes) envelope treatment for non-primitive user messages.
    /// </summary>
    public sealed record PocoPayload(string Name, int Count);

    /// <summary>
    /// Superset of the legacy <see cref="ReliableDeliverySerializerSpecs"/> message matrix: the
    /// original cases (the parity contract) plus POCO-payload envelope cases and additional
    /// empty/edge shapes.
    /// </summary>
    public static IEnumerable<object[]> ReliableDeliveryMsgs()
    {
        yield return
        [
            "SequencedMessage-1",
            new ConsumerController.SequencedMessage<string>("prod-1", 17L, "msg17", false, false)
        ];
        yield return
        [
            "SequencedMessage-2", new ConsumerController.SequencedMessage<string>("prod-1", 1L, "msg01", true, true)
        ];
        yield return
        [
            "SequencedMessage-poco",
            new ConsumerController.SequencedMessage<PocoPayload>("prod-2", 2L, new PocoPayload("poco", 42), false, true)
        ];
        yield return ["Ack", new ProducerController.Ack(5L)];
        yield return ["Request", new ProducerController.Request(5L, 25L, true, true)];
        yield return ["Resend", new ProducerController.Resend(5L)];
        yield return
        [
            "RegisterConsumer", new ProducerController.RegisterConsumer<(int, double)>(ActorRefs
                .Nobody) // using a nested tuple type to test the serializer's reflection capabilities
        ];
        yield return
        [
            "DurableProducerQueue.MessageSent-1",
            new DurableProducerQueue.MessageSent<string>(3L, "msg03", false, "", Timestamp)
        ];
        yield return
        [
            "DurableProducerQueue.MessageSent-2",
            new DurableProducerQueue.MessageSent<string>(3L, "msg03", true, "q1", Timestamp)
        ];
        yield return
        [
            "DurableProducerQueue.MessageSent-poco",
            new DurableProducerQueue.MessageSent<PocoPayload>(4L, new PocoPayload("poco", 17), true, "q9", Timestamp)
        ];
        yield return
        [
            "DurableProducerQueue.Confirmed", new DurableProducerQueue.Confirmed(3L, "q2", Timestamp)
        ];
        yield return
        [
            "DurableProducerQueue.State-1", new DurableProducerQueue.State<string>(3L, 2L,
                ImmutableDictionary<string, (long, long)>.Empty,
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty)
        ];
        yield return
        [
            "DurableProducerQueue.State-2", new DurableProducerQueue.State<string>(3L, 2L,
                ImmutableDictionary<string, (long, long)>.Empty.Add("", (2L, Timestamp)),
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty.Add(
                    new DurableProducerQueue.MessageSent<string>(3L, "msg03", false, "", Timestamp)))
        ];
        yield return
        [
            "DurableProducerQueue.State-3", new DurableProducerQueue.State<string>(17L, 12L,
                ImmutableDictionary<string, (long, long)>.Empty.Add("q1", (5L, Timestamp)).Add("q2", (7L, Timestamp))
                    .Add("q3", (12L, Timestamp))
                    .Add("q4", (14L, Timestamp)),
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty.Add(
                        new DurableProducerQueue.MessageSent<string>(15L, "msg15", true, "q4", Timestamp))
                    .Add(
                        new DurableProducerQueue.MessageSent<string>(16L, "msg16", true, "q4", Timestamp)))
        ];
        yield return
        [
            "DurableProducerQueue.State-4-chunked-unconfirmed", new DurableProducerQueue.State<string>(6L, 4L,
                ImmutableDictionary<string, (long, long)>.Empty.Add("q1", (4L, Timestamp)),
                ImmutableList<DurableProducerQueue.MessageSent<string>>.Empty.Add(
                    DurableProducerQueue.MessageSent<string>.FromChunked(5L,
                        new ChunkedMessage(new byte[] { 9, 8, 7 }.AsMemory(), true, false, 17, "C"), true, "q1",
                        Timestamp)))
        ];
        yield return
        [
            "DurableProducerQueue.Cleanup",
            new DurableProducerQueue.Cleanup(new[] { "q1", "q2", "q3" }.ToImmutableHashSet())
        ];
        yield return
        [
            "DurableProducerQueue.Cleanup-empty",
            new DurableProducerQueue.Cleanup(ImmutableHashSet<string>.Empty)
        ];
        yield return
        [
            "SequencedMessage-chunked-1",
            ConsumerController.SequencedMessage<string>.FromChunkedMessage("prod-1", 1L,
                new ChunkedMessage("abc"u8.ToArray().AsMemory(), true, true, 20, ""), true, true, ActorRefs.Nobody)
        ];
        yield return
        [
            "SequencedMessage-chunked-2",
            ConsumerController.SequencedMessage<string>.FromChunkedMessage("prod-1", 1L,
                new ChunkedMessage(new byte[] { 1, 2, 3 }.AsMemory(), true, false, 123456, "A"), false, false,
                ActorRefs.Nobody)
        ];
        yield return
        [
            "DurableProducerQueue.MessageSent-chunked",
            DurableProducerQueue.MessageSent<string>.FromChunked(3L,
                new ChunkedMessage("abc"u8.ToArray().AsMemory(), true, true, 20, ""), false, "", Timestamp)
        ];
    }

    [Theory]
    [MemberData(nameof(ReliableDeliveryMsgs))]
    public void MessagePack_serializer_should_round_trip_ReliableDelivery_msgs(string scenario,
        IDeliverySerializable rawMsg)
    {
        Sys.Log.Info(scenario);
        var msg = WithRealActorRefs(rawMsg);

        // manifest parity with the legacy serializer - manifests are wire contracts
        var manifest = MsgPackSerializer.Manifest(msg);
        manifest.Should().Be(LegacySerializer.Manifest(msg),
            "the MessagePack fork must reuse the legacy serializer's manifest tokens");

        var bytes = MsgPackSerializer.ToBinary(msg);
        var deserialized = MsgPackSerializer.FromBinary(bytes, manifest);
        deserialized.Should().BeEquivalentTo(msg);
    }

    [Theory]
    [MemberData(nameof(ReliableDeliveryMsgs))]
    public void Legacy_serializer_should_still_round_trip_ReliableDelivery_msgs(string scenario,
        IDeliverySerializable rawMsg)
    {
        Sys.Log.Info(scenario);
        var msg = WithRealActorRefs(rawMsg);
        var deserialized = LegacySerializer.FromBinary(LegacySerializer.ToBinary(msg), LegacySerializer.Manifest(msg));
        deserialized.Should().BeEquivalentTo(msg);
    }

    [Theory]
    [MemberData(nameof(ReliableDeliveryMsgs))]
    public void Both_serializers_should_be_resolvable_by_id_from_one_ActorSystem(string scenario,
        IDeliverySerializable rawMsg)
    {
        Sys.Log.Info(scenario);
        var msg = WithRealActorRefs(rawMsg);

        // reads dispatch purely by serializer id - a node holding both registrations can decode
        // either wire format, which is the entire dual-registration rolling-upgrade contract
        var legacyBytes = LegacySerializer.ToBinary(msg);
        Sys.Serialization.Deserialize(legacyBytes, ReliableDeliverySerializerId, LegacySerializer.Manifest(msg))
            .Should().BeEquivalentTo(msg);

        var msgPackBytes = MsgPackSerializer.ToBinary(msg);
        Sys.Serialization
            .Deserialize(msgPackBytes, ReliableDeliveryMessagePackSerializer.SerializerIdentifierValue,
                MsgPackSerializer.Manifest(msg))
            .Should().BeEquivalentTo(msg);
    }

    [Theory]
    [MemberData(nameof(ReliableDeliveryMsgs))]
    public void Writes_should_remain_bound_to_the_legacy_protobuf_serializer(string scenario,
        IDeliverySerializable msg)
    {
        Sys.Log.Info(scenario);
        // serialization-bindings still point IDeliverySerializable at the protobuf serializer -
        // the MessagePack serializer is registered additively (read-side only) until the
        // write-side flag infrastructure flips the binding
        Sys.Serialization.FindSerializerForType(msg.GetType()).Should().BeOfType<ReliableDeliverySerializer>();
    }

    [Fact]
    public void MessagePack_serializer_should_use_the_reserved_forked_id()
    {
        MsgPackSerializer.Identifier.Should().Be(76, "forked id = legacy id 36 + 40 (reserved 40-79 block)");
        LegacySerializer.Identifier.Should().Be(36);
        ReliableDeliveryMessagePackSerializer.SerializerIdentifierValue.Should()
            .Be(ReliableDeliverySerializerId + 40);
    }

    [Fact]
    public void MessagePack_serializer_should_treat_Nobody_producer_controller_like_the_legacy_serializer()
    {
        // Nobody serializes to its /Nobody path and resolves through the provider on read - neither
        // serializer maps it back to Nobody.Instance identity. The parity contract is that the fork
        // resolves EXACTLY what the legacy serializer resolves.
        var msg = ConsumerController.SequencedMessage<string>.FromChunkedMessage("prod-1", 1L,
            new ChunkedMessage("abc"u8.ToArray().AsMemory(), true, true, 20, ""), true, true, ActorRefs.Nobody);

        var fromLegacy = (ConsumerController.SequencedMessage<string>)LegacySerializer.FromBinary(
            LegacySerializer.ToBinary(msg), LegacySerializer.Manifest(msg));
        var fromMsgPack = (ConsumerController.SequencedMessage<string>)MsgPackSerializer.FromBinary(
            MsgPackSerializer.ToBinary(msg), MsgPackSerializer.Manifest(msg));

        fromMsgPack.Should().BeEquivalentTo(fromLegacy);
        fromMsgPack.ProducerController.Path.Should().Be(fromLegacy.ProducerController.Path);
    }

    private const int ReliableDeliverySerializerId = 36;

    private object WithRealActorRefs(IDeliverySerializable msg)
    {
        switch (msg)
        {
            case ConsumerController.SequencedMessage<string> sequencedMessage:
                return sequencedMessage with { ProducerController = RealActorRef };
            case ConsumerController.SequencedMessage<PocoPayload> sequencedMessage:
                return sequencedMessage with { ProducerController = RealActorRef };
            case ProducerController.RegisterConsumer<(int, double)> _:
                return new ProducerController.RegisterConsumer<(int, double)>(RealActorRef);
            default:
                return msg;
        }
    }
}
