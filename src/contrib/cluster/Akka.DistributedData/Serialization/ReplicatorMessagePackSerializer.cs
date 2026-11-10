// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.DistributedData.Durable;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Akka.Serialization.V2;
using Google.Protobuf;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Serialization;

/// <summary>
/// Generated MessagePack fork of the Replicator protocol, id 52. Legacy id 12 remains registered
/// for reads. Nested CRDTs and application payloads retain their own serializer id and manifest;
/// this port does not change the id 11 CRDT format or establish generic CRDT support under AOT.
/// </summary>
internal sealed class ReplicatorMessagePackSerializer : SerializerV2
{
    internal const int SerializerIdentifier = 52;
    private readonly ReplicatorMessagePackCodec _codec;

    public ReplicatorMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ReplicatorMessagePackCodec(system);
    }

    public override int Identifier => SerializerIdentifier;

    public override string Manifest(object obj) => obj switch
    {
        Get => "A",
        GetSuccess => "B",
        NotFound => "C",
        GetFailure => "D",
        Subscribe => "E",
        Unsubscribe => "F",
        Changed => "G",
        DataEnvelope => "H",
        Write => "I",
        WriteAck => "J",
        Read => "K",
        ReadResult => "L",
        Internal.Status => "M",
        Gossip => "N",
        WriteNack => "O",
        DurableDataEnvelope => "P",
        DeltaPropagation => "Q",
        DeltaNack => "R",
        _ => throw new ArgumentException($"Unsupported Replicator message [{obj.GetType()}].", nameof(obj))
    };

    public override byte[] ToBinary(object obj)
    {
        var writer = new ArrayBufferWriter<byte>();
        Serialize(obj, writer);
        return writer.WrittenSpan.ToArray();
    }

    public override int Serialize(object obj, IBufferWriter<byte> writer)
    {
        var previous = Akka.Serialization.Serialization.CurrentTransportInformation;
        try
        {
            // Digest and durable-store writes bypass remoting, but nested actor refs still need
            // their full address. Preserve an ambient transport context exactly as legacy does.
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;

            var wire = ToWire(obj);
            if (obj is not Gossip)
                return _codec.Serialize(wire, writer);

            // Gossip stays compressed at the wrapper boundary, as in the legacy protocol.
            var uncompressed = _codec.ToBinary(wire);
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
                gzip.Write(uncompressed);
            var bytes = compressed.ToArray();
            writer.Write(bytes);
            return bytes.Length;
        }
        finally
        {
            Akka.Serialization.Serialization.CurrentTransportInformation = previous;
        }
    }

    public override object Deserialize(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (manifest != "N")
            return FromWire(_codec.Deserialize(bytes, manifest));

        using var compressed = new MemoryStream(bytes.ToArray(), writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var uncompressed = new MemoryStream();
        gzip.CopyTo(uncompressed);
        return FromWire(_codec.Deserialize(new ReadOnlySequence<byte>(uncompressed.ToArray()), manifest));
    }

    private object ToWire(object obj) => obj switch
    {
        Get get => ToWire(get),
        GetSuccess get => new ReplicatorGetSuccessWire(get.Key, get.Data, get.Request),
        NotFound get => new ReplicatorNotFoundWire(get.Key, get.Request),
        GetFailure get => new ReplicatorGetFailureWire(get.Key, get.Request),
        Subscribe subscribe => new ReplicatorSubscribeWire(subscribe.Key, subscribe.Subscriber),
        Unsubscribe unsubscribe => new ReplicatorUnsubscribeWire(unsubscribe.Key, unsubscribe.Subscriber),
        Changed changed => new ReplicatorChangedWire(changed.Key, changed.Data),
        DataEnvelope envelope => ToWire(envelope),
        Write write => new ReplicatorWriteWire(write.Key, ToWire(write.Envelope), ToWire(write.FromNode)),
        WriteAck => new ReplicatorWriteAckWire(),
        Read read => new ReplicatorReadWire(read.Key, ToWire(read.FromNode)),
        ReadResult read => new ReplicatorReadResultWire(read.Envelope is null ? null : ToWire(read.Envelope)),
        Internal.Status status => new ReplicatorStatusWire(status.Chunk, status.TotalChunks,
            status.Digests.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new ReplicatorDigestWire(e.Key, e.Value.ToByteArray())).ToArray(),
            status.ToSystemUid, status.FromSystemUid),
        Gossip gossip => new ReplicatorGossipWire(gossip.SendBack,
            gossip.UpdatedData.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new ReplicatorGossipEntryWire(e.Key, ToWire(e.Value))).ToArray(),
            gossip.ToSystemUid, gossip.FromSystemUid),
        WriteNack => new ReplicatorWriteNackWire(),
        DurableDataEnvelope durable => new ReplicatorDurableEnvelopeWire(durable.Data,
            ToWirePruning(durable.DataEnvelope.Pruning.Where(e => e.Value is PruningPerformed))),
        DeltaPropagation delta => new ReplicatorDeltaPropagationWire(ToWire(delta.FromNode)!, delta.ShouldReply,
            delta.Deltas.OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => new ReplicatorDeltaEntryWire(e.Key, ToWire(e.Value.DataEnvelope),
                    e.Value.FromSeqNr, e.Value.ToSeqNr)).ToArray()),
        DeltaNack => new ReplicatorDeltaNackWire(),
        _ => throw new ArgumentException($"Unsupported Replicator message [{obj.GetType()}].", nameof(obj))
    };

    private static ReplicatorGetWire ToWire(Get get)
    {
        var milliseconds = get.Consistency.Timeout.TotalMilliseconds;
        if (milliseconds > uint.MaxValue || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(get), "Timeouts must fit in a 32-bit unsigned int.");

        // Explicit kinds keep ReadFrom(1) and ReadMajorityPlus(additional: 0) distinct from
        // ReadLocal and ReadMajority. These are permanent V2 discriminators, not CLR type names.
        return get.Consistency switch
        {
            ReadLocal => new ReplicatorGetWire(get.Key, 0, (uint)milliseconds, 0, 0, 0, get.Request),
            ReadFrom read => new ReplicatorGetWire(get.Key, 1, (uint)milliseconds, read.N, 0, 0, get.Request),
            ReadMajority read => new ReplicatorGetWire(get.Key, 2, (uint)milliseconds, 0, read.MinCapacity, 0, get.Request),
            ReadMajorityPlus read => new ReplicatorGetWire(get.Key, 3, (uint)milliseconds, 0, read.MinCapacity, read.Additional, get.Request),
            ReadAll => new ReplicatorGetWire(get.Key, 4, (uint)milliseconds, 0, 0, 0, get.Request),
            _ => throw new ArgumentException($"Unsupported read consistency [{get.Consistency.GetType()}].", nameof(get))
        };
    }

    private static ReplicatorDataEnvelopeWire ToWire(DataEnvelope envelope)
    {
        var versions = new List<KeyValuePair<UniqueAddress, long>>();
        using (var iterator = envelope.DeltaVersions.VersionEnumerator)
        {
            while (iterator.MoveNext())
                versions.Add(iterator.Current);
        }

        return new ReplicatorDataEnvelopeWire(envelope.Data, ToWirePruning(envelope.Pruning),
            OrderNodes(versions).Select(e => new ReplicatorVersionEntryWire(ToWire(e.Key)!, e.Value)).ToArray());
    }

    private static ReplicatorPruningWire[] ToWirePruning(IEnumerable<KeyValuePair<UniqueAddress, IPruningState>> pruning)
        => OrderNodes(pruning).Select(e => e.Value switch
        {
            PruningPerformed performed => new ReplicatorPruningWire(ToWire(e.Key)!, true,
                performed.ObsoleteTime.Ticks, null, []),
            PruningInitialized initialized => new ReplicatorPruningWire(ToWire(e.Key)!, false, 0,
                ToWire(initialized.Owner), initialized.Seen.OrderBy(a => a.Host, StringComparer.Ordinal)
                    .ThenBy(a => a.Port).Select(ToWireAddress).ToArray()),
            _ => throw new ArgumentException($"Unsupported pruning state [{e.Value.GetType()}].", nameof(pruning))
        }).ToArray();

    private static IOrderedEnumerable<KeyValuePair<UniqueAddress, T>> OrderNodes<T>(IEnumerable<KeyValuePair<UniqueAddress, T>> entries)
        => entries.OrderBy(e => e.Key.Address.Host, StringComparer.Ordinal)
            .ThenBy(e => e.Key.Address.Port).ThenBy(e => e.Key.Uid);

    private static ReplicatorUniqueAddressWire? ToWire(UniqueAddress? node) => node is null
        ? null
        : new ReplicatorUniqueAddressWire(ToWireAddress(node.Address), node.Uid);

    private static ReplicatorAddressWire ToWireAddress(Address address)
    {
        if (string.IsNullOrEmpty(address.Host) || !address.Port.HasValue)
            throw new ArgumentOutOfRangeException(nameof(address), "Address host and port must be defined.");
        return new ReplicatorAddressWire(address.Host, address.Port.Value);
    }

    private object FromWire(object wire) => wire switch
    {
        ReplicatorGetWire get => new Get((IKey)get.Key, FromWireConsistency(get), get.Request),
        ReplicatorGetSuccessWire get => new GetSuccess((IKey)get.Key, get.Request, (IReplicatedData)get.Data),
        ReplicatorNotFoundWire get => new NotFound((IKey)get.Key, get.Request),
        ReplicatorGetFailureWire get => new GetFailure((IKey)get.Key, get.Request),
        ReplicatorSubscribeWire subscribe => new Subscribe((IKey)subscribe.Key, subscribe.Subscriber),
        ReplicatorUnsubscribeWire unsubscribe => new Unsubscribe((IKey)unsubscribe.Key, unsubscribe.Subscriber),
        ReplicatorChangedWire changed => new Changed((IKey)changed.Key, changed.Data),
        ReplicatorDataEnvelopeWire envelope => FromWire(envelope),
        ReplicatorWriteWire write => new Write(write.Key, FromWire(write.Envelope), FromWire(write.FromNode)),
        ReplicatorWriteAckWire => WriteAck.Instance,
        ReplicatorReadWire read => new Read(read.Key, FromWire(read.FromNode)),
        ReplicatorReadResultWire read => new ReadResult(read.Envelope is null ? null : FromWire(read.Envelope)),
        ReplicatorStatusWire status => new Internal.Status(status.Entries.ToImmutableDictionary(e => e.Key,
            e => ByteString.CopyFrom(e.Digest)), status.Chunk, status.TotalChunks, status.ToSystemUid, status.FromSystemUid),
        ReplicatorGossipWire gossip => new Gossip(gossip.Entries.ToImmutableDictionary(e => e.Key,
            e => FromWire(e.Envelope)), gossip.SendBack, gossip.ToSystemUid, gossip.FromSystemUid),
        ReplicatorWriteNackWire => WriteNack.Instance,
        ReplicatorDurableEnvelopeWire durable => new DurableDataEnvelope(new DataEnvelope(
            (IReplicatedData)durable.Data, FromWirePruning(durable.Pruning))),
        ReplicatorDeltaPropagationWire delta => new DeltaPropagation(FromWire(delta.FromNode)!, delta.ShouldReply,
            delta.Entries.ToImmutableDictionary(e => e.Key,
                e => new Delta(FromWire(e.Envelope), e.FromSeqNr, e.ToSeqNr))),
        ReplicatorDeltaNackWire => DeltaNack.Instance,
        _ => throw new SerializationException($"Unsupported Replicator wire message [{wire.GetType()}].")
    };

    private static IReadConsistency FromWireConsistency(ReplicatorGetWire get)
    {
        var timeout = TimeSpan.FromMilliseconds(get.TimeoutMilliseconds);
        return get.Kind switch
        {
            0 => ReadLocal.Instance,
            1 => new ReadFrom(get.N, timeout),
            2 => new ReadMajority(timeout, get.MinCapacity),
            3 => new ReadMajorityPlus(timeout, get.Additional, get.MinCapacity),
            4 => new ReadAll(timeout),
            _ => throw new SerializationException($"Unsupported read consistency discriminator [{get.Kind}].")
        };
    }

    private DataEnvelope FromWire(ReplicatorDataEnvelopeWire envelope) => new((IReplicatedData)envelope.Data,
        FromWirePruning(envelope.Pruning), VersionVector.Create(envelope.DeltaVersions.ToImmutableDictionary(
            e => FromWire(e.Node)!, e => e.Version)));

    private ImmutableDictionary<UniqueAddress, IPruningState> FromWirePruning(ReplicatorPruningWire[] entries)
        => entries.ToImmutableDictionary(e => FromWire(e.Removed)!, e => e.Performed
            ? (IPruningState)new PruningPerformed(new DateTime(e.ObsoleteTicks))
            : new PruningInitialized(FromWire(e.Owner) ?? throw new SerializationException("A pruning owner is required."),
                e.Seen.Select(FromWire).ToImmutableHashSet()));

    private UniqueAddress? FromWire(ReplicatorUniqueAddressWire? node)
        => node is null ? null : new UniqueAddress(FromWire(node.Address), node.Uid);

    private Address FromWire(ReplicatorAddressWire address)
        => new(system.Provider.DefaultAddress.Protocol, system.Name, address.Hostname, address.Port);
}

internal interface IReplicatorWireMessage
{
}

[AkkaSerializer<IReplicatorWireMessage>("akka-data-replication-codec", ReplicatorMessagePackSerializer.SerializerIdentifier)]
internal sealed partial class ReplicatorMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

// Permanent field ids. The address fragments intentionally carry host/port and Int64 UID, just
// as legacy does. Protocol/system are reconstructed from the receiving system. All unordered
// wrapper collections are encoded as sorted arrays; nested payload bytes remain their codec's contract.
[AkkaSerializable(Manifest = "A")]
internal sealed record ReplicatorGetWire(
    [property: AkkaField(1)] object Key, [property: AkkaField(2)] int Kind,
    [property: AkkaField(3)] uint TimeoutMilliseconds, [property: AkkaField(4)] int N,
    [property: AkkaField(5)] int MinCapacity, [property: AkkaField(6)] int Additional,
    [property: AkkaField(7)] object? Request) : IReplicatorWireMessage;

[AkkaSerializable(Manifest = "B")]
internal sealed record ReplicatorGetSuccessWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] object Data, [property: AkkaField(3)] object? Request) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "C")]
internal sealed record ReplicatorNotFoundWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] object? Request) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "D")]
internal sealed record ReplicatorGetFailureWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] object? Request) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "E")]
internal sealed record ReplicatorSubscribeWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] IActorRef Subscriber) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "F")]
internal sealed record ReplicatorUnsubscribeWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] IActorRef Subscriber) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "G")]
internal sealed record ReplicatorChangedWire([property: AkkaField(1)] object Key,
    [property: AkkaField(2)] object Data) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "H")]
internal sealed record ReplicatorDataEnvelopeWire([property: AkkaField(1)] object Data,
    [property: AkkaField(2)] ReplicatorPruningWire[] Pruning,
    [property: AkkaField(3)] ReplicatorVersionEntryWire[] DeltaVersions) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "I")]
internal sealed record ReplicatorWriteWire([property: AkkaField(1)] string Key,
    [property: AkkaField(2)] ReplicatorDataEnvelopeWire Envelope,
    [property: AkkaField(3)] ReplicatorUniqueAddressWire? FromNode) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "J", AllowEmpty = true)]
internal sealed record ReplicatorWriteAckWire : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "K")]
internal sealed record ReplicatorReadWire([property: AkkaField(1)] string Key,
    [property: AkkaField(2)] ReplicatorUniqueAddressWire? FromNode) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "L")]
internal sealed record ReplicatorReadResultWire([property: AkkaField(1)] ReplicatorDataEnvelopeWire? Envelope) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "M")]
internal sealed record ReplicatorStatusWire([property: AkkaField(1)] int Chunk,
    [property: AkkaField(2)] int TotalChunks, [property: AkkaField(3)] ReplicatorDigestWire[] Entries,
    [property: AkkaField(4)] long? ToSystemUid, [property: AkkaField(5)] long? FromSystemUid) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "N")]
internal sealed record ReplicatorGossipWire([property: AkkaField(1)] bool SendBack,
    [property: AkkaField(2)] ReplicatorGossipEntryWire[] Entries,
    [property: AkkaField(3)] long? ToSystemUid, [property: AkkaField(4)] long? FromSystemUid) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "O", AllowEmpty = true)]
internal sealed record ReplicatorWriteNackWire : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "P")]
internal sealed record ReplicatorDurableEnvelopeWire([property: AkkaField(1)] object Data,
    [property: AkkaField(2)] ReplicatorPruningWire[] Pruning) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "Q")]
internal sealed record ReplicatorDeltaPropagationWire([property: AkkaField(1)] ReplicatorUniqueAddressWire FromNode,
    [property: AkkaField(2)] bool ShouldReply, [property: AkkaField(3)] ReplicatorDeltaEntryWire[] Entries) : IReplicatorWireMessage;
[AkkaSerializable(Manifest = "R", AllowEmpty = true)]
internal sealed record ReplicatorDeltaNackWire : IReplicatorWireMessage;

[AkkaSerializable]
internal sealed record ReplicatorAddressWire([property: AkkaField(1)] string Hostname, [property: AkkaField(2)] int Port);
[AkkaSerializable]
internal sealed record ReplicatorUniqueAddressWire([property: AkkaField(1)] ReplicatorAddressWire Address,
    [property: AkkaField(2)] long Uid);
[AkkaSerializable]
internal sealed record ReplicatorVersionEntryWire([property: AkkaField(1)] ReplicatorUniqueAddressWire Node,
    [property: AkkaField(2)] long Version);
[AkkaSerializable]
internal sealed record ReplicatorPruningWire([property: AkkaField(1)] ReplicatorUniqueAddressWire Removed,
    [property: AkkaField(2)] bool Performed, [property: AkkaField(3)] long ObsoleteTicks,
    [property: AkkaField(4)] ReplicatorUniqueAddressWire? Owner, [property: AkkaField(5)] ReplicatorAddressWire[] Seen);
[AkkaSerializable]
internal sealed record ReplicatorDigestWire([property: AkkaField(1)] string Key, [property: AkkaField(2)] byte[] Digest);
[AkkaSerializable]
internal sealed record ReplicatorGossipEntryWire([property: AkkaField(1)] string Key,
    [property: AkkaField(2)] ReplicatorDataEnvelopeWire Envelope);
[AkkaSerializable]
internal sealed record ReplicatorDeltaEntryWire([property: AkkaField(1)] string Key,
    [property: AkkaField(2)] ReplicatorDataEnvelopeWire Envelope,
    [property: AkkaField(3)] long FromSeqNr, [property: AkkaField(4)] long ToSeqNr);
