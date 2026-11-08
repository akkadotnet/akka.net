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
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Akka.Util;
using MessagePack;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Serialization;

/// <summary>
/// Generated MessagePack CRDT protocol at id 51. Primitive closed forms have static factories;
/// other generic shapes require dynamic loading. Canonical legacy protobuf remains a separate path.
/// </summary>
internal sealed partial class ReplicatedDataMessagePackSerializer : SerializerV2
{
    internal const int SerializerIdentifier = 51;
    private readonly ReplicatedDataMessagePackCodec _codec;

    public ReplicatedDataMessagePackSerializer(ExtendedActorSystem system) : base(system)
    {
        _codec = new ReplicatedDataMessagePackCodec(system);
    }

    public override int Identifier => SerializerIdentifier;

    public override string Manifest(object obj) => obj switch
    {
        IORSet => "C", ORSet.IAddDeltaOperation => "Ca", ORSet.IRemoveDeltaOperation => "Cr",
        IGSet => "B", GCounter => "F", PNCounter => "G", Flag => "D", ILWWRegister => "E",
        IORDictionary => "H", ORDictionary.IPutDeltaOp => "Ha", ORDictionary.IRemoveDeltaOp => "Hr",
        ORDictionary.IRemoveKeyDeltaOp => "Hk", ORDictionary.IUpdateDeltaOp => "Hu",
        ILWWDictionary => "I", ILWWDictionaryDeltaOperation => "Ig",
        IPNCounterDictionary => "J", IPNCounterDictionaryDeltaOperation => "Jo",
        IORMultiValueDictionary => "K", IORMultiValueDictionaryDeltaOperation => "Ko",
        DeletedData => "A", VersionVector => "L",
        IORSetKey => "c", IGSetKey => "b", GCounterKey => "f", PNCounterKey => "g", FlagKey => "d",
        ILWWRegisterKey => "e", IORDictionaryKey => "h", ILWWDictionaryKey => "i",
        IPNCounterDictionaryKey => "j", IORMultiValueDictionaryKey => "k",
        ORSet.IDeltaGroupOperation => "Cg", ORDictionary.IDeltaGroupOp => "Hg", ORSet.IFullStateDeltaOperation => "Cf",
        _ => throw new ArgumentException($"Unsupported replicated data [{obj.GetType()}].", nameof(obj))
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
            if (previous is null)
                Akka.Serialization.Serialization.CurrentTransportInformation = system.Provider.SerializationInformation;
            // Projection (including generic guards) precedes all writes to the caller's buffer.
            var wire = ToWire(obj);
            if (!Compressed(Manifest(obj)))
            {
                if (!FallibleEnvelope(wire))
                    return _codec.Serialize(wire, writer);
                using var staged = new PooledPayloadWriter();
                var count = _codec.Serialize(wire, staged);
                if (count != staged.WrittenCount)
                    throw new SerializationException("Replicated-data codec reported an incorrect byte count.");
                writer.Write(staged.WrittenSpan);
                return count;
            }
            var payload = _codec.ToBinary(wire);
            using var output = new MemoryStream();
            using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                gzip.Write(payload);
            var bytes = output.ToArray();
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
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported && DynamicManifest(manifest))
            throw DynamicError();
        if (!Compressed(manifest))
        {
            RejectDynamicDescriptor(bytes, manifest);
            return FromWire(_codec.Deserialize(bytes, manifest));
        }
        using var input = new MemoryStream(bytes.ToArray(), writable: false);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        var payload = new ReadOnlySequence<byte>(output.ToArray());
        RejectDynamicDescriptor(payload, manifest);
        return FromWire(_codec.Deserialize(payload, manifest));
    }

    private static bool Compressed(string manifest) => manifest is "C" or "H" or "I" or "J" or "K";

    private static bool FallibleEnvelope(object wire) => wire switch
    {
        CrdtRegisterWire => true,
        CrdtGSetWire set => FallibleElements(set.Elements),
        CrdtORSetAddWire op => FallibleElements(op.State.Elements),
        CrdtORSetRemoveWire op => FallibleElements(op.State.Elements),
        CrdtORSetFullWire op => FallibleElements(op.State.Elements),
        CrdtORSetGroupWire group => group.Operations.Any(op => FallibleElements(op.State.Elements)),
        CrdtDictionaryPutWire or CrdtDictionaryRemoveWire or CrdtDictionaryRemoveKeyWire or CrdtDictionaryUpdateWire
            or CrdtDictionaryGroupWire or CrdtLWWDictionaryDeltaWire or CrdtPNDictionaryDeltaWire or CrdtMultiDictionaryDeltaWire => true,
        _ => false
    };
    private static bool FallibleElements(CrdtElementsWire elements) => elements.Others.Length != 0 || elements.Refs.Length != 0;

    private static bool DynamicManifest(string manifest)
        => manifest is "H" or "I" or "J" or "K" or "Ha" or "Hr" or "Hk" or "Hu" or "Hg" or "Ig" or "Jo" or "Ko" or "h" or "i" or "k";

    private static readonly int[] ElementTypePath = [1, 1, 1];
    private static readonly int[] DirectTypePath = [1, 1];
    private static readonly int[] DeltaTypePath = [1, 1, 1, 1];
    private static readonly int[] KeyTypePath = [2, 1];

    private static void RejectDynamicDescriptor(ReadOnlySequence<byte> bytes, string manifest)
    {
        if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            return;
        ReadOnlySpan<int> path = manifest switch
        {
            "B" or "C" => ElementTypePath,
            "E" or "Cg" => DirectTypePath,
            "Ca" or "Cr" or "Cf" => DeltaTypePath,
            "b" or "c" or "e" or "j" => KeyTypePath,
            _ => []
        };
        if (path.IsEmpty)
            return;
        // Inspect metadata without invoking an opaque payload decoder, even if the sender put
        // payload fields before the type descriptor. Generated decoding happens only afterward.
        var reader = new MessagePackReader(bytes);
        if (ReadKind(ref reader, path) == 4)
            throw DynamicError();
    }

    private static int ReadKind(ref MessagePackReader reader, ReadOnlySpan<int> path)
    {
        var count = reader.ReadMapHeader();
        var kind = 0;
        for (var index = 0; index < count; index++)
        {
            var field = reader.ReadInt32();
            if (field != path[0])
                reader.Skip();
            else
                kind = path.Length == 1 ? reader.ReadInt32() : ReadKind(ref reader, path[1..]);
        }
        return kind;
    }

    private object ToWire(object obj) => obj switch
    {
        DeletedData => new CrdtDeletedWire(),
        Flag flag => new CrdtFlagWire(flag.Enabled),
        GCounter counter => ToCounter(counter),
        PNCounter counter => new CrdtPNCounterWire(ToCounter(counter.Increments), ToCounter(counter.Decrements)),
        VersionVector vector => new CrdtVersionVectorWire(ToVector(vector)),
        IGSet set => ToGSet(set),
        IORSet set => ToORSet(set),
        ILWWRegister register => ToRegister(register),
        IORDictionary map => Dynamic(nameof(WriteDictionary), map, map.KeyType, map.ValueType),
        ILWWDictionary map => Dynamic(nameof(WriteLWWDictionary), map, map.KeyType, map.ValueType),
        IPNCounterDictionary map => Dynamic(nameof(WritePNDictionary), map, map.KeyType),
        IORMultiValueDictionary map => Dynamic(nameof(WriteMultiDictionary), map, map.KeyType, map.ValueType),
        ORSet.IAddDeltaOperation op => new CrdtORSetAddWire(ToORSet(op.UnderlyingSerialization)),
        ORSet.IRemoveDeltaOperation op => new CrdtORSetRemoveWire(ToORSet(op.UnderlyingSerialization)),
        ORSet.IFullStateDeltaOperation op => new CrdtORSetFullWire(ToORSet(op.UnderlyingSerialization)),
        ORSet.IDeltaGroupOperation group => ToSetGroup(group),
        ORDictionary.IPutDeltaOp op => new CrdtDictionaryPutWire(ToMapOperations(op)),
        ORDictionary.IRemoveDeltaOp op => new CrdtDictionaryRemoveWire(ToMapOperations(op)),
        ORDictionary.IRemoveKeyDeltaOp op => new CrdtDictionaryRemoveKeyWire(ToMapOperations(op)),
        ORDictionary.IUpdateDeltaOp op => new CrdtDictionaryUpdateWire(ToMapOperations(op)),
        ORDictionary.IDeltaGroupOp op => new CrdtDictionaryGroupWire(ToMapOperations(op)),
        ILWWDictionaryDeltaOperation op => new CrdtLWWDictionaryDeltaWire(ToMapOperations(op.Underlying)),
        IPNCounterDictionaryDeltaOperation op => new CrdtPNDictionaryDeltaWire(ToMapOperations(op.Underlying)),
        IORMultiValueDictionaryDeltaOperation op => new CrdtMultiDictionaryDeltaWire(ToMapOperations(op.Underlying), op.WithValueDeltas),
        IKey key => ToKey(key),
        _ => throw new ArgumentException($"Unsupported replicated data [{obj.GetType()}].", nameof(obj))
    };

    private object FromWire(object obj) => obj switch
    {
        CrdtDeletedWire => DeletedData.Instance,
        CrdtFlagWire flag => flag.Enabled ? Flag.True : Flag.False,
        CrdtCounterWire counter => FromCounter(counter),
        CrdtPNCounterWire counter => new PNCounter(FromCounter(counter.Increments), FromCounter(counter.Decrements)),
        CrdtVersionVectorWire vector => FromVector(vector.Entries),
        CrdtGSetWire set => ReadSetKind(nameof(ReadGSet), set, set.Elements.Type),
        CrdtORSetWire set => ReadSetKind(nameof(ReadORSet), set, set.Elements.Type),
        CrdtRegisterWire register => ReadSetKind(nameof(ReadRegister), register, register.Type),
        CrdtORSetAddWire op => ReadSetKind(nameof(ReadSetAdd), op.State, op.State.Elements.Type),
        CrdtORSetRemoveWire op => ReadSetKind(nameof(ReadSetRemove), op.State, op.State.Elements.Type),
        CrdtORSetFullWire op => ReadSetKind(nameof(ReadSetFull), op.State, op.State.Elements.Type),
        CrdtORSetGroupWire group => ReadSetKind(nameof(ReadSetGroup), group, group.Type),
        CrdtDictionaryWire map => Dynamic(nameof(ReadDictionary), map, Resolve(map.KeySet.Elements.Type), Resolve(map.ValueType)),
        CrdtLWWDictionaryWire map => Dynamic(nameof(ReadLWWDictionary), map, Resolve(map.KeySet.Elements.Type), Resolve(map.ValueType)),
        CrdtPNDictionaryWire map => Dynamic(nameof(ReadPNDictionary), map, Resolve(map.KeySet.Elements.Type)),
        CrdtMultiDictionaryWire map => Dynamic(nameof(ReadMultiDictionary), map, Resolve(map.KeySet.Elements.Type), Resolve(map.ValueType)),
        CrdtDictionaryPutWire op => ReadMapOperations(op.Operations, 0),
        CrdtDictionaryRemoveWire op => ReadMapOperations(op.Operations, 1),
        CrdtDictionaryRemoveKeyWire op => ReadMapOperations(op.Operations, 2),
        CrdtDictionaryUpdateWire op => ReadMapOperations(op.Operations, 3),
        CrdtDictionaryGroupWire op => ReadMapOperations(op.Operations, 4),
        CrdtLWWDictionaryDeltaWire op => ReadMapWrapper(op.Operations, "lww", false),
        CrdtPNDictionaryDeltaWire op => ReadMapWrapper(op.Operations, "pn", false),
        CrdtMultiDictionaryDeltaWire op => ReadMapWrapper(op.Operations, "multi", op.WithValueDeltas),
        CrdtCounterKeyWire key => new GCounterKey(key.Id),
        CrdtPNCounterKeyWire key => new PNCounterKey(key.Id),
        CrdtFlagKeyWire key => new FlagKey(key.Id),
        _ => FromKey(obj)
    };

    private CrdtGSetWire ToGSet(IGSet set) => (CrdtGSetWire)(set switch
    {
        GSet<int> => WriteGSet<int>(set), GSet<long> => WriteGSet<long>(set),
        GSet<string> => WriteGSet<string>(set), GSet<IActorRef> => WriteGSet<IActorRef>(set),
        _ => Dynamic(nameof(WriteGSet), set, set.SetType)
    });

    private CrdtORSetWire ToORSet(IORSet set) => (CrdtORSetWire)(set switch
    {
        ORSet<int> => WriteORSet<int>(set), ORSet<long> => WriteORSet<long>(set),
        ORSet<string> => WriteORSet<string>(set), ORSet<IActorRef> => WriteORSet<IActorRef>(set),
        _ => Dynamic(nameof(WriteORSet), set, set.SetType)
    });

    private CrdtRegisterWire ToRegister(ILWWRegister register) => (CrdtRegisterWire)(register switch
    {
        LWWRegister<int> => WriteRegister<int>(register), LWWRegister<long> => WriteRegister<long>(register),
        LWWRegister<string> => WriteRegister<string>(register), LWWRegister<IActorRef> => WriteRegister<IActorRef>(register),
        _ => Dynamic(nameof(WriteRegister), register, register.RegisterType)
    });

    private object ReadSetKind(string operation, object wire, CrdtTypeWire type) => (operation, type.Kind) switch
    {
        (nameof(ReadGSet), 0) => ReadGSet<int>(wire), (nameof(ReadGSet), 1) => ReadGSet<long>(wire),
        (nameof(ReadGSet), 2) => ReadGSet<string>(wire), (nameof(ReadGSet), 3) => ReadGSet<IActorRef>(wire),
        (nameof(ReadORSet), 0) => ReadORSet<int>(wire), (nameof(ReadORSet), 1) => ReadORSet<long>(wire),
        (nameof(ReadORSet), 2) => ReadORSet<string>(wire), (nameof(ReadORSet), 3) => ReadORSet<IActorRef>(wire),
        (nameof(ReadRegister), 0) => ReadRegister<int>(wire), (nameof(ReadRegister), 1) => ReadRegister<long>(wire),
        (nameof(ReadRegister), 2) => ReadRegister<string>(wire), (nameof(ReadRegister), 3) => ReadRegister<IActorRef>(wire),
        (nameof(ReadSetAdd), 0) => ReadSetAdd<int>(wire), (nameof(ReadSetAdd), 1) => ReadSetAdd<long>(wire),
        (nameof(ReadSetAdd), 2) => ReadSetAdd<string>(wire), (nameof(ReadSetAdd), 3) => ReadSetAdd<IActorRef>(wire),
        (nameof(ReadSetRemove), 0) => ReadSetRemove<int>(wire), (nameof(ReadSetRemove), 1) => ReadSetRemove<long>(wire),
        (nameof(ReadSetRemove), 2) => ReadSetRemove<string>(wire), (nameof(ReadSetRemove), 3) => ReadSetRemove<IActorRef>(wire),
        (nameof(ReadSetFull), 0) => ReadSetFull<int>(wire), (nameof(ReadSetFull), 1) => ReadSetFull<long>(wire),
        (nameof(ReadSetFull), 2) => ReadSetFull<string>(wire), (nameof(ReadSetFull), 3) => ReadSetFull<IActorRef>(wire),
        (nameof(ReadSetGroup), 0) => ReadSetGroup<int>(wire), (nameof(ReadSetGroup), 1) => ReadSetGroup<long>(wire),
        (nameof(ReadSetGroup), 2) => ReadSetGroup<string>(wire), (nameof(ReadSetGroup), 3) => ReadSetGroup<IActorRef>(wire),
        _ => Dynamic(operation, wire, Resolve(type))
    };

    private static CrdtCounterWire ToCounter(GCounter counter) => new(OrderNodes(counter.State)
        .Select(entry => new CrdtCountWire(ToNode(entry.Key), entry.Value)).ToArray());
    private GCounter FromCounter(CrdtCounterWire counter) => new(counter.Entries.ToImmutableDictionary(entry => FromNode(entry.Node), entry => entry.Value));

    private static ReplicatorUniqueAddressWire ToNode(UniqueAddress node)
    {
        var address = node.Address;
        if (string.IsNullOrEmpty(address.Host) || !address.Port.HasValue)
            throw new ArgumentOutOfRangeException(nameof(node), "Address host and port must be defined.");
        return new ReplicatorUniqueAddressWire(new ReplicatorAddressWire(address.Host, address.Port.Value), node.Uid);
    }
    private UniqueAddress FromNode(ReplicatorUniqueAddressWire node) => new(
        new Address(system.Provider.DefaultAddress.Protocol, system.Name, node.Address.Hostname, node.Address.Port), node.Uid);

    private static ReplicatorVersionEntryWire[] ToVector(VersionVector vector)
    {
        var entries = new List<KeyValuePair<UniqueAddress, long>>();
        using var iterator = vector.VersionEnumerator;
        while (iterator.MoveNext()) entries.Add(iterator.Current);
        return OrderNodes(entries).Select(entry => new ReplicatorVersionEntryWire(ToNode(entry.Key), entry.Value)).ToArray();
    }
    private VersionVector FromVector(ReplicatorVersionEntryWire[] entries) => VersionVector.Create(entries.ToImmutableDictionary(entry => FromNode(entry.Node), entry => entry.Version));
    private static IOrderedEnumerable<KeyValuePair<UniqueAddress, T>> OrderNodes<T>(IEnumerable<KeyValuePair<UniqueAddress, T>> entries)
        => entries.OrderBy(entry => entry.Key.Address.Host, StringComparer.Ordinal).ThenBy(entry => entry.Key.Address.Port).ThenBy(entry => entry.Key.Uid);

    private IActorRef Reference(IActorRef? value) => value ?? system.Provider.ResolveActorRef(string.Empty);
}
