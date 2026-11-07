// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System;
using System.Buffers;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe.Internal;
using Akka.Serialization;
using Akka.Serialization.V2;
using Status = Akka.Cluster.Tools.PublishSubscribe.Internal.Status;

namespace Akka.Cluster.Tools.PublishSubscribe.Serialization;

/// <summary>
/// Generated MessagePack companion to the legacy PubSub serializer (id 9). Id 49 is read-only
/// until akka.actor.serialization-v2 is enabled. User payloads keep their own serializer envelope.
/// </summary>
internal sealed class DistributedPubSubMessagePackSerializer : SerializerV2
{
    internal const int SerializerId = 49;
    private readonly DistributedPubSubMessagePackCodec _codec;

    public DistributedPubSubMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new DistributedPubSubMessagePackCodec(system);
    }

    public override int Identifier => SerializerId;

    public override string Manifest(object obj) => obj switch
    {
        Status => "A",
        Delta => "B",
        Send => "C",
        SendToAll => "D",
        Publish => "E",
        SendToOneSubscriber => "F",
        _ => throw new ArgumentException($"Cannot serialize [{obj.GetType()}] with [{GetType()}]", nameof(obj))
    };

    public override int Serialize(object obj, IBufferWriter<byte> writer) => _codec.Serialize(ToWire(obj), writer);

    public override int SizeHint(object obj) => _codec.SizeHint(ToWire(obj));

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest) => _codec.Deserialize(bytes, manifest) switch
    {
        PubSubStatusWire status => new Status(status.Versions, status.IsReplyToStatus),
        PubSubDeltaWire delta => new Delta(delta.Buckets.Select(bucket => new Bucket(bucket.Owner, bucket.Version,
            bucket.Content.ToImmutableDictionary(pair => pair.Key, pair => new ValueHolder(pair.Value.Version, pair.Value.Ref!))))
            .ToImmutableList()),
        PubSubSendWire send => new Send(send.Path, send.Message, send.LocalAffinity),
        PubSubSendToAllWire all => new SendToAll(all.Path, all.Message, all.ExcludeSelf),
        PubSubPublishWire publish => new Publish(publish.Topic, publish.Message, publish.SendOneMessageToEachGroup),
        PubSubSendToOneSubscriberWire subscriber => new SendToOneSubscriber(subscriber.Message),
        var wire => throw new SerializationException($"Unknown PubSub wire message [{wire.GetType()}]")
    };

    private static IPubSubWireMessage ToWire(object obj) => obj switch
    {
        Status status => new PubSubStatusWire(status.Versions.ToImmutableDictionary(), status.IsReplyToStatus),
        Delta delta => new PubSubDeltaWire(delta.Buckets.Select(bucket => new PubSubBucketWire(bucket.Owner, bucket.Version,
            bucket.Content.ToImmutableDictionary(pair => pair.Key, pair => new PubSubValueWire(pair.Value.Version, pair.Value.Ref))))
            .ToImmutableList()),
        Send send => new PubSubSendWire(send.Path, send.Message, send.LocalAffinity),
        SendToAll all => new PubSubSendToAllWire(all.Path, all.Message, all.ExcludeSelf),
        Publish publish => new PubSubPublishWire(publish.Topic, publish.Message, publish.SendOneMessageToEachGroup),
        SendToOneSubscriber subscriber => new PubSubSendToOneSubscriberWire(subscriber.Message),
        PublishWithAck => throw new SerializationException("Distributed PubSub does not support serializing PublishWithAck"),
        _ => throw new ArgumentException($"Cannot serialize [{obj.GetType()}] with [{typeof(DistributedPubSubMessagePackSerializer)}]", nameof(obj))
    };
}

internal interface IPubSubWireMessage;

/// <summary>The wire codec is owned by the domain wrapper and is never registered separately.</summary>
[AkkaSerializer<IPubSubWireMessage>("akka-pubsub-v2-codec", DistributedPubSubMessagePackSerializer.SerializerId)]
[AkkaSerializerFormatter<Address, AddressFormatter>]
internal sealed partial class DistributedPubSubMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "A")]
internal sealed record PubSubStatusWire(
    [property: AkkaField(1)] ImmutableDictionary<Address, long> Versions,
    [property: AkkaField(2)] bool IsReplyToStatus) : IPubSubWireMessage;

[AkkaSerializable(Manifest = "B")]
internal sealed record PubSubDeltaWire(
    [property: AkkaField(1)] ImmutableList<PubSubBucketWire> Buckets) : IPubSubWireMessage;

[AkkaSerializable]
internal sealed record PubSubBucketWire(
    [property: AkkaField(1)] Address Owner,
    [property: AkkaField(2)] long Version,
    [property: AkkaField(3)] ImmutableDictionary<string, PubSubValueWire> Content);

[AkkaSerializable]
internal sealed record PubSubValueWire(
    [property: AkkaField(1)] long Version,
    [property: AkkaField(2)] IActorRef? Ref);

[AkkaSerializable(Manifest = "C")]
internal sealed record PubSubSendWire(
    [property: AkkaField(1)] string Path,
    [property: AkkaField(2)] object Message,
    [property: AkkaField(3)] bool LocalAffinity) : IPubSubWireMessage;

[AkkaSerializable(Manifest = "D")]
internal sealed record PubSubSendToAllWire(
    [property: AkkaField(1)] string Path,
    [property: AkkaField(2)] object Message,
    [property: AkkaField(3)] bool ExcludeSelf) : IPubSubWireMessage;

[AkkaSerializable(Manifest = "E")]
internal sealed record PubSubPublishWire(
    [property: AkkaField(1)] string Topic,
    [property: AkkaField(2)] object Message,
    [property: AkkaField(3)] bool SendOneMessageToEachGroup) : IPubSubWireMessage;

[AkkaSerializable(Manifest = "F")]
internal sealed record PubSubSendToOneSubscriberWire(
    [property: AkkaField(1)] object Message) : IPubSubWireMessage;
