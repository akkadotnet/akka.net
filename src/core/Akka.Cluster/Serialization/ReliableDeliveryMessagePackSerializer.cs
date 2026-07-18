//-----------------------------------------------------------------------
// <copyright file="ReliableDeliveryMessagePackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Delivery;
using Akka.Delivery.Internal;
using Akka.Serialization;
using Akka.Serialization.V2;
using Akka.Util;

namespace Akka.Cluster.Serialization;

/// <summary>
/// INTERNAL API
///
/// Source-generated MessagePack (Akka.Serialization.V2) fork of <see cref="ReliableDeliverySerializer"/>
/// (serializer id 36, protobuf). This serializer covers the exact same message set and manifests
/// ("a".."i") under NEW serializer id 76 (legacy id + 40, the reserved 40-79 internal V2 block).
///
/// <para>
/// Wire-compat model: dual registration. The legacy protobuf serializer stays fully registered and
/// is readable forever; this serializer is additive. Reads dispatch purely by serializer id, so any
/// v1.6 node can decode BOTH formats. Writes stay on protobuf until the separately-built
/// <c>akka.actor.serialization.v2</c> write-side flag re-points the
/// <c>Akka.Delivery.Internal.IDeliverySerializable</c> binding at this serializer.
/// </para>
/// <para>
/// Structure: the reliable-delivery domain messages are generic (<c>SequencedMessage&lt;T&gt;</c>,
/// <c>MessageSent&lt;T&gt;</c>, ...) and live in core Akka, which cannot reference
/// Akka.Serialization.V2 - so they cannot be annotated with <c>[AkkaSerializable]</c> directly.
/// Instead this class translates domain object &lt;-&gt; non-generic <c>[AkkaSerializable]</c> wire
/// mirror (below) and delegates the MessagePack encoding to the source-generated
/// <see cref="ReliableDeliveryMessagePackCodec"/>, exactly as the protobuf serializer translates
/// domain object &lt;-&gt; generated proto message. Nested user payloads are serializer boundaries:
/// the codec's <c>[AkkaEnvelopePayload]</c> fields preserve the (serializerId, manifest, bytes)
/// triple, the direct analog of the protobuf side's <c>WrappedPayloadSupport</c>. Chunked payloads
/// ride through as raw bytes.
/// </para>
/// <para>
/// Unlike the legacy serializer, the write path here is reflection-free: the internal
/// <c>ISequencedMessage</c>/<c>IMessageSent</c>/<c>IState</c>/<c>IRegisterConsumer</c> interfaces
/// expose non-generic member access. The read path must still construct the generic domain types;
/// it uses one cached <see cref="IDomainFactory"/> per payload type instead of per-call
/// <c>MakeGenericMethod</c> reflection.
/// </para>
/// </summary>
internal sealed class ReliableDeliveryMessagePackSerializer : SerializerV2
{
    /// <summary>
    /// Serializer id 76 = legacy <see cref="ReliableDeliverySerializer"/> id 36 + 40 (the reserved
    /// 40-79 block for internal V2/MessagePack forks). Once shipped this id is permanent - it may
    /// exist in durable producer-queue journals.
    /// </summary>
    internal const int SerializerIdentifierValue = 76;

    // Manifest tokens are identical to ReliableDeliverySerializer's - they are serializer-scoped,
    // so reuse is safe and keeps intra-serializer dispatch a 1:1 mirror of the protobuf fork.
    private const string SequencedMessageManifest = "a";
    private const string AckManifest = "b";
    private const string RequestManifest = "c";
    private const string ResendManifest = "d";
    private const string RegisterConsumerManifest = "e";

    // durable queue manifests
    private const string DurableQueueMessageSentManifest = "f";
    private const string DurableQueueConfirmedManifest = "g";
    private const string DurableQueueStateManifest = "h";
    private const string DurableQueueCleanupManifest = "i";

    private static readonly ConcurrentDictionary<Type, IDomainFactory> DomainFactories = new();
    private static readonly ConcurrentDictionary<string, Type> TypeCache = new();

    private readonly ReliableDeliveryMessagePackCodec _codec;

    public ReliableDeliveryMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ReliableDeliveryMessagePackCodec(system);
    }

    public override int Identifier => SerializerIdentifierValue;

    public override string Manifest(object obj)
    {
        switch (obj)
        {
            case ConsumerController.ISequencedMessage _:
                return SequencedMessageManifest;
            case ProducerController.Ack _:
                return AckManifest;
            case ProducerController.Request _:
                return RequestManifest;
            case ProducerController.Resend _:
                return ResendManifest;
            case ProducerController.IRegisterConsumer _:
                return RegisterConsumerManifest;
            case DurableProducerQueue.IMessageSent _:
                return DurableQueueMessageSentManifest;
            case DurableProducerQueue.Confirmed _:
                return DurableQueueConfirmedManifest;
            case DurableProducerQueue.IState _:
                return DurableQueueStateManifest;
            case DurableProducerQueue.Cleanup _:
                return DurableQueueCleanupManifest;
            default:
                throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        return _codec.Serialize(ToWire(obj), writer);
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        return FromWire(_codec.Deserialize(bytes, manifest));
    }

    // SizeHint deliberately stays at the SerializerV2 default (UnknownSize): computing an exact
    // size on the OUTER serializer would require a second domain->wire conversion per message.
    // ToBinary below converts once and sizes off the codec's exact wire-level SizeHint instead.

    public override byte[] ToBinary(object obj)
    {
        var wire = ToWire(obj);
        var sizeHint = _codec.SizeHint(wire);
        var writer = sizeHint > 0 ? new ArrayBufferWriter<byte>(sizeHint) : new ArrayBufferWriter<byte>();
        _codec.Serialize(wire, writer);
        return writer.WrittenMemory.ToArray();
    }

    #region domain -> wire

    private object ToWire(object obj)
    {
        switch (obj)
        {
            case ConsumerController.ISequencedMessage sequencedMessage:
                return new SequencedMessageWire(
                    sequencedMessage.PayloadType.TypeQualifiedName(),
                    sequencedMessage.ProducerId,
                    sequencedMessage.SeqNr,
                    sequencedMessage.First,
                    sequencedMessage.Ack,
                    sequencedMessage.ProducerControllerRef,
                    sequencedMessage.Payload,
                    ToWire(sequencedMessage.Chunk));
            case ProducerController.Ack ack:
                return new AckWire(ack.ConfirmedSeqNr);
            case ProducerController.Request request:
                return new RequestWire(request.ConfirmedSeqNr, request.RequestUpToSeqNr, request.SupportResend,
                    request.ViaTimeout);
            case ProducerController.Resend resend:
                return new ResendWire(resend.FromSeqNr);
            case ProducerController.IRegisterConsumer registerConsumer:
                return new RegisterConsumerWire(
                    registerConsumer.ConsumerType.TypeQualifiedName(),
                    registerConsumer.ConsumerController);
            case DurableProducerQueue.IMessageSent messageSent:
                return ToWire(messageSent, messageSent.MessageType.TypeQualifiedName());
            case DurableProducerQueue.Confirmed confirmed:
                return new ConfirmedWire(confirmed.SeqNr, confirmed.Qualifier, confirmed.Timestamp);
            case DurableProducerQueue.IState state:
                {
                    var confirmed = new ConfirmedWire[state.ConfirmedSeqNr.Count];
                    var confirmedIndex = 0;
                    foreach (var kvp in state.ConfirmedSeqNr)
                        confirmed[confirmedIndex++] = new ConfirmedWire(kvp.Value.Item1, kvp.Key, kvp.Value.Item2);

                    var typeName = state.MessageType.TypeQualifiedName();
                    var unconfirmedMessages = state.UnconfirmedMessages;
                    var unconfirmed = new MessageSentWire[unconfirmedMessages.Count];
                    for (var i = 0; i < unconfirmed.Length; i++)
                        unconfirmed[i] = ToWire(unconfirmedMessages[i], typeName);

                    return new StateWire(state.CurrentSeqNr, state.HighestConfirmedSeqNr, confirmed, unconfirmed,
                        typeName);
                }
            case DurableProducerQueue.Cleanup cleanup:
                {
                    var qualifiers = new string[cleanup.ConfirmationQualifiers.Count];
                    cleanup.ConfirmationQualifiers.CopyTo(qualifiers, 0);
                    return new CleanupWire(qualifiers);
                }
            default:
                throw new ArgumentException($"Unimplemented serialization of message [{obj.GetType()}] in [{GetType()}]");
        }
    }

    private static MessageSentWire ToWire(DurableProducerQueue.IMessageSent messageSent, string typeName)
    {
        return new MessageSentWire(
            messageSent.SeqNr,
            messageSent.ConfirmationQualifier,
            messageSent.Ack,
            messageSent.Timestamp,
            messageSent.Payload,
            ToWire(messageSent.Chunk),
            typeName);
    }

    private static ChunkedMessageWire? ToWire(ChunkedMessage? chunk)
    {
        if (chunk is not { } chunkedMessage)
            return null;

        return new ChunkedMessageWire(
            chunkedMessage.SerializedMessage.ToArray(),
            chunkedMessage.FirstChunk,
            chunkedMessage.LastChunk,
            chunkedMessage.SerializerId,
            chunkedMessage.Manifest);
    }

    #endregion

    #region wire -> domain

    private object FromWire(object wire)
    {
        switch (wire)
        {
            case SequencedMessageWire sequencedMessage:
                return GetFactory(ResolveType(sequencedMessage.TypeName))
                    .CreateSequencedMessage(sequencedMessage, sequencedMessage.ProducerControllerRef ?? ActorRefs.Nobody);
            case AckWire ack:
                return new ProducerController.Ack(ack.ConfirmedSeqNr);
            case RequestWire request:
                return new ProducerController.Request(request.ConfirmedSeqNr, request.RequestUpToSeqNr,
                    request.SupportResend, request.ViaTimeout);
            case ResendWire resend:
                return new ProducerController.Resend(resend.FromSeqNr);
            case RegisterConsumerWire registerConsumer:
                return GetFactory(ResolveType(registerConsumer.TypeName))
                    .CreateRegisterConsumer(registerConsumer.ConsumerControllerRef ?? ActorRefs.Nobody);
            case MessageSentWire messageSent:
                return GetFactory(ResolveType(messageSent.TypeName)).CreateMessageSent(messageSent);
            case ConfirmedWire confirmed:
                return new DurableProducerQueue.Confirmed(confirmed.SeqNr, confirmed.Qualifier, confirmed.Timestamp);
            case StateWire state:
                return GetFactory(ResolveType(state.TypeName)).CreateState(state);
            case CleanupWire cleanup:
                return new DurableProducerQueue.Cleanup(cleanup.Qualifiers.ToImmutableHashSet());
            default:
                throw new ArgumentException($"Unimplemented deserialization of wire message [{wire.GetType()}] in [{GetType()}]");
        }
    }

    private static ChunkedMessage FromWire(ChunkedMessageWire wire)
    {
        return new ChunkedMessage(wire.SerializedMessage, wire.FirstChunk, wire.LastChunk, wire.SerializerId,
            wire.Manifest);
    }

    private static Type ResolveType(string typeName)
    {
        return TypeCache.GetOrAdd(typeName, static name => Type.GetType(name, true)!);
    }

    private static IDomainFactory GetFactory(Type payloadType)
    {
        return DomainFactories.GetOrAdd(payloadType,
            static t => (IDomainFactory)Activator.CreateInstance(typeof(DomainFactory<>).MakeGenericType(t))!);
    }

    /// <summary>
    /// Rehydrates the generic reliable-delivery domain types for one payload type. One instance is
    /// created (reflectively) and cached per payload type - all subsequent reads are reflection-free.
    /// </summary>
    private interface IDomainFactory
    {
        ConsumerController.ISequencedMessage CreateSequencedMessage(SequencedMessageWire wire, IActorRef producerController);
        DurableProducerQueue.IMessageSent CreateMessageSent(MessageSentWire wire);
        DurableProducerQueue.IState CreateState(StateWire wire);
        ProducerController.IRegisterConsumer CreateRegisterConsumer(IActorRef consumerController);
    }

    private sealed class DomainFactory<T> : IDomainFactory
    {
        public ConsumerController.ISequencedMessage CreateSequencedMessage(SequencedMessageWire wire, IActorRef producerController)
        {
            if (wire.Chunk is { } chunk)
                return ConsumerController.SequencedMessage<T>.FromChunkedMessage(wire.ProducerId, wire.SeqNr,
                    FromWire(chunk), wire.First, wire.Ack, producerController);

            return new ConsumerController.SequencedMessage<T>(wire.ProducerId, wire.SeqNr, (T)wire.Payload!,
                wire.First, wire.Ack, producerController);
        }

        public DurableProducerQueue.IMessageSent CreateMessageSent(MessageSentWire wire)
        {
            if (wire.Chunk is { } chunk)
                return DurableProducerQueue.MessageSent<T>.FromChunked(wire.SeqNr, FromWire(chunk), wire.Ack,
                    wire.Qualifier, wire.Timestamp);

            return new DurableProducerQueue.MessageSent<T>(wire.SeqNr, (T)wire.Payload!, wire.Ack, wire.Qualifier,
                wire.Timestamp);
        }

        public DurableProducerQueue.IState CreateState(StateWire wire)
        {
            var confirmed = ImmutableDictionary.CreateBuilder<string, (long, long)>();
            foreach (var entry in wire.Confirmed)
                confirmed.Add(entry.Qualifier, (entry.SeqNr, entry.Timestamp));

            var unconfirmed = ImmutableList.CreateBuilder<DurableProducerQueue.MessageSent<T>>();
            foreach (var messageSent in wire.Unconfirmed)
                unconfirmed.Add((DurableProducerQueue.MessageSent<T>)CreateMessageSent(messageSent));

            return new DurableProducerQueue.State<T>(wire.CurrentSeqNr, wire.HighestConfirmedSeqNr,
                confirmed.ToImmutable(), unconfirmed.ToImmutable());
        }

        public ProducerController.IRegisterConsumer CreateRegisterConsumer(IActorRef consumerController)
        {
            return new ProducerController.RegisterConsumer<T>(consumerController);
        }
    }

    #endregion
}

/// <summary>
/// INTERNAL API
///
/// Protocol marker for the source-generated <see cref="ReliableDeliveryMessagePackCodec"/>. Only
/// the wire mirror records below implement it - the codec never sees domain types.
/// </summary>
internal interface IReliableDeliveryWireMessage
{
}

/// <summary>
/// INTERNAL API
///
/// Source-generated MessagePack codec for the reliable-delivery wire mirrors. NOT registered with
/// the serialization subsystem: <see cref="ReliableDeliveryMessagePackSerializer"/> owns id 76 and
/// delegates the byte-level work here. The generator
/// (<c>Akka.Serialization.V2.Generators.AkkaSerializerGenerator</c>, attached as an analyzer on
/// <c>Akka.Cluster.csproj</c>) emits the other half of this partial class - constructor,
/// <c>Identifier</c>, <c>Manifest</c>/<c>Serialize</c>/<c>Deserialize</c>/<c>SizeHint</c> dispatch,
/// and one Write/Read/SizeOf method per wire record.
/// </summary>
[AkkaSerializer<IReliableDeliveryWireMessage>("reliable-delivery-message-pack-codec",
    ReliableDeliveryMessagePackSerializer.SerializerIdentifierValue)]
internal sealed partial class ReliableDeliveryMessagePackCodec : AkkaSerializer
{
    /// <summary>
    /// Generated by <c>Akka.Serialization.V2.Generators.AkkaSerializerGenerator</c>. Unused - the
    /// codec is instantiated directly by <see cref="ReliableDeliveryMessagePackSerializer"/>, never
    /// registered on its own.
    /// </summary>
    public static partial SerializerRegistration CreateRegistration();
}

// ---------------------------------------------------------------------------------------------
// Wire mirrors. Field ids and manifests are PERMANENT WIRE FORMAT once shipped - extend-only.
// The shapes mirror src/protobuf/ReliableDelivery.proto field-for-field, with two deltas:
//  * the protobuf Payload sub-message (WrappedPayloadSupport) becomes an [AkkaEnvelopePayload]
//    field (same (serializerId, manifest, bytes) triple, MessagePack-framed);
//  * the chunk flags live on ChunkedMessageWire instead of duplicated top-level
//    firstChunk/lastChunk/isChunk booleans (they are derived properties on the domain types).
// ---------------------------------------------------------------------------------------------

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ConsumerController.SequencedMessage{T}"/> (manifest "a").
/// Exactly one of <see cref="Payload"/> (user message, opaque envelope) or <see cref="Chunk"/>
/// (chunked transfer segment) is non-null.
/// </summary>
[AkkaSerializable(Manifest = "a")]
internal sealed record SequencedMessageWire(
    [property: AkkaField(1)] string TypeName,
    [property: AkkaField(2)] string ProducerId,
    [property: AkkaField(3)] long SeqNr,
    [property: AkkaField(4)] bool First,
    [property: AkkaField(5)] bool Ack,
    [property: AkkaField(6)] IActorRef? ProducerControllerRef,
    [property: AkkaField(7), AkkaEnvelopePayload] object? Payload,
    [property: AkkaField(8)] ChunkedMessageWire? Chunk) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ProducerController.Ack"/> (manifest "b").
/// </summary>
[AkkaSerializable(Manifest = "b")]
internal sealed record AckWire(
    [property: AkkaField(1)] long ConfirmedSeqNr) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ProducerController.Request"/> (manifest "c").
/// </summary>
[AkkaSerializable(Manifest = "c")]
internal sealed record RequestWire(
    [property: AkkaField(1)] long ConfirmedSeqNr,
    [property: AkkaField(2)] long RequestUpToSeqNr,
    [property: AkkaField(3)] bool SupportResend,
    [property: AkkaField(4)] bool ViaTimeout) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ProducerController.Resend"/> (manifest "d").
/// </summary>
[AkkaSerializable(Manifest = "d")]
internal sealed record ResendWire(
    [property: AkkaField(1)] long FromSeqNr) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ProducerController.RegisterConsumer{T}"/> (manifest "e").
/// </summary>
[AkkaSerializable(Manifest = "e")]
internal sealed record RegisterConsumerWire(
    [property: AkkaField(1)] string TypeName,
    [property: AkkaField(2)] IActorRef? ConsumerControllerRef) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="DurableProducerQueue.MessageSent{T}"/> (manifest "f");
/// also nested inline inside <see cref="StateWire.Unconfirmed"/>. Exactly one of
/// <see cref="Payload"/> or <see cref="Chunk"/> is non-null.
/// </summary>
[AkkaSerializable(Manifest = "f")]
internal sealed record MessageSentWire(
    [property: AkkaField(1)] long SeqNr,
    [property: AkkaField(2)] string Qualifier,
    [property: AkkaField(3)] bool Ack,
    [property: AkkaField(4)] long Timestamp,
    [property: AkkaField(5), AkkaEnvelopePayload] object? Payload,
    [property: AkkaField(6)] ChunkedMessageWire? Chunk,
    [property: AkkaField(7)] string TypeName) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="DurableProducerQueue.Confirmed"/> (manifest "g");
/// also nested inline inside <see cref="StateWire.Confirmed"/>.
/// </summary>
[AkkaSerializable(Manifest = "g")]
internal sealed record ConfirmedWire(
    [property: AkkaField(1)] long SeqNr,
    [property: AkkaField(2)] string Qualifier,
    [property: AkkaField(3)] long Timestamp) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="DurableProducerQueue.State{T}"/> (manifest "h").
/// </summary>
[AkkaSerializable(Manifest = "h")]
internal sealed record StateWire(
    [property: AkkaField(1)] long CurrentSeqNr,
    [property: AkkaField(2)] long HighestConfirmedSeqNr,
    [property: AkkaField(3)] ConfirmedWire[] Confirmed,
    [property: AkkaField(4)] MessageSentWire[] Unconfirmed,
    [property: AkkaField(5)] string TypeName) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="DurableProducerQueue.Cleanup"/> (manifest "i").
/// </summary>
[AkkaSerializable(Manifest = "i")]
internal sealed record CleanupWire(
    [property: AkkaField(1)] string[] Qualifiers) : IReliableDeliveryWireMessage;

/// <summary>
/// INTERNAL API - wire mirror of <see cref="ChunkedMessage"/> (nested value, no manifest). The
/// already-serialized chunk bytes ride through opaquely; <see cref="SerializerId"/> and
/// <see cref="Manifest"/> describe the ORIGINAL user-message serializer used before chunking.
/// </summary>
[AkkaSerializable]
internal sealed record ChunkedMessageWire(
    [property: AkkaField(1)] byte[] SerializedMessage,
    [property: AkkaField(2)] bool FirstChunk,
    [property: AkkaField(3)] bool LastChunk,
    [property: AkkaField(4)] int SerializerId,
    [property: AkkaField(5)] string Manifest);
