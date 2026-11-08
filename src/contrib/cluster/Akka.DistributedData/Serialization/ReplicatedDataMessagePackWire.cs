// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using Akka.Actor;
using Akka.Serialization;
using Akka.Serialization.V2;

namespace Akka.DistributedData.Serialization;

internal interface IReplicatedDataWireMessage
{
}

[AkkaSerializer<IReplicatedDataWireMessage>("akka-replicated-data-v2-codec", ReplicatedDataMessagePackSerializer.SerializerIdentifier)]
internal sealed partial class ReplicatedDataMessagePackCodec : AkkaSerializer
{
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

[AkkaSerializable(Manifest = "A", AllowEmpty = true)]
internal sealed record CrdtDeletedWire : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "B")]
internal sealed record CrdtGSetWire([property: AkkaField(1)] CrdtElementsWire Elements) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "C")]
internal sealed record CrdtORSetWire(
    [property: AkkaField(1)] CrdtElementsWire Elements,
    [property: AkkaField(2)] ReplicatorVersionEntryWire[] Vector,
    [property: AkkaField(3)] CrdtDotWire[] Dots) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "D")]
internal sealed record CrdtFlagWire([property: AkkaField(1)] bool Enabled) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "E")]
internal sealed record CrdtRegisterWire(
    [property: AkkaField(1)] CrdtTypeWire Type,
    [property: AkkaField(2)] object? Value,
    [property: AkkaField(3)] ReplicatorUniqueAddressWire UpdatedBy,
    [property: AkkaField(4)] long Timestamp) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "F")]
internal sealed record CrdtCounterWire([property: AkkaField(1)] CrdtCountWire[] Entries) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "G")]
internal sealed record CrdtPNCounterWire(
    [property: AkkaField(1)] CrdtCounterWire Increments,
    [property: AkkaField(2)] CrdtCounterWire Decrements) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "H")]
internal sealed record CrdtDictionaryWire(
    [property: AkkaField(1)] CrdtORSetWire KeySet,
    [property: AkkaField(2)] CrdtElementsWire EntryKeys,
    [property: AkkaField(3)] CrdtTypeWire ValueType,
    [property: AkkaField(4)] object?[] Values) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "I")]
internal sealed record CrdtLWWDictionaryWire(
    [property: AkkaField(1)] CrdtORSetWire KeySet,
    [property: AkkaField(2)] CrdtElementsWire EntryKeys,
    [property: AkkaField(3)] CrdtTypeWire ValueType,
    [property: AkkaField(4)] CrdtRegisterWire[] Values) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "J")]
internal sealed record CrdtPNDictionaryWire(
    [property: AkkaField(1)] CrdtORSetWire KeySet,
    [property: AkkaField(2)] CrdtElementsWire EntryKeys,
    [property: AkkaField(3)] CrdtPNCounterWire[] Values) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "K")]
internal sealed record CrdtMultiDictionaryWire(
    [property: AkkaField(1)] CrdtORSetWire KeySet,
    [property: AkkaField(2)] CrdtElementsWire EntryKeys,
    [property: AkkaField(3)] CrdtTypeWire ValueType,
    [property: AkkaField(4)] CrdtORSetWire[] Values,
    [property: AkkaField(5)] bool WithValueDeltas) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "L")]
internal sealed record CrdtVersionVectorWire([property: AkkaField(1)] ReplicatorVersionEntryWire[] Entries) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "Ca")]
internal sealed record CrdtORSetAddWire([property: AkkaField(1)] CrdtORSetWire State) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Cr")]
internal sealed record CrdtORSetRemoveWire([property: AkkaField(1)] CrdtORSetWire State) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Cf")]
internal sealed record CrdtORSetFullWire([property: AkkaField(1)] CrdtORSetWire State) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Cg")]
internal sealed record CrdtORSetGroupWire(
    [property: AkkaField(1)] CrdtTypeWire Type,
    [property: AkkaField(2)] CrdtORSetOperationWire[] Operations) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "Ha")]
internal sealed record CrdtDictionaryPutWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Hr")]
internal sealed record CrdtDictionaryRemoveWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Hk")]
internal sealed record CrdtDictionaryRemoveKeyWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Hu")]
internal sealed record CrdtDictionaryUpdateWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Hg")]
internal sealed record CrdtDictionaryGroupWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Ig")]
internal sealed record CrdtLWWDictionaryDeltaWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Jo")]
internal sealed record CrdtPNDictionaryDeltaWire([property: AkkaField(1)] CrdtMapOperationsWire Operations) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "Ko")]
internal sealed record CrdtMultiDictionaryDeltaWire(
    [property: AkkaField(1)] CrdtMapOperationsWire Operations,
    [property: AkkaField(2)] bool WithValueDeltas) : IReplicatedDataWireMessage;

[AkkaSerializable(Manifest = "b")]
internal sealed record CrdtGSetKeyWire([property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire Type) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "c")]
internal sealed record CrdtORSetKeyWire([property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire Type) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "d")]
internal sealed record CrdtFlagKeyWire([property: AkkaField(1)] string Id) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "e")]
internal sealed record CrdtRegisterKeyWire([property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire Type) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "f")]
internal sealed record CrdtCounterKeyWire([property: AkkaField(1)] string Id) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "g")]
internal sealed record CrdtPNCounterKeyWire([property: AkkaField(1)] string Id) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "h")]
internal sealed record CrdtDictionaryKeyWire(
    [property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire KeyType,
    [property: AkkaField(3)] CrdtTypeWire ValueType) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "i")]
internal sealed record CrdtLWWDictionaryKeyWire(
    [property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire KeyType,
    [property: AkkaField(3)] CrdtTypeWire ValueType) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "j")]
internal sealed record CrdtPNDictionaryKeyWire([property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire Type) : IReplicatedDataWireMessage;
[AkkaSerializable(Manifest = "k")]
internal sealed record CrdtMultiDictionaryKeyWire(
    [property: AkkaField(1)] string Id, [property: AkkaField(2)] CrdtTypeWire KeyType,
    [property: AkkaField(3)] CrdtTypeWire ValueType) : IReplicatedDataWireMessage;

// Primitive kinds retain the legacy meaning: 0=int, 1=long, 2=string, 3=actor ref,
// 4=another declared CLR type. Values use typed arrays to avoid an envelope per scalar.
[AkkaSerializable]
internal sealed record CrdtTypeWire([property: AkkaField(1)] int Kind, [property: AkkaField(2)] string? Name);

[AkkaSerializable]
internal sealed record CrdtElementsWire(
    [property: AkkaField(1)] CrdtTypeWire Type,
    [property: AkkaField(2)] int[] Ints,
    [property: AkkaField(3)] long[] Longs,
    [property: AkkaField(4)] string[] Strings,
    [property: AkkaField(5)] IActorRef?[] Refs,
    [property: AkkaField(6)] object?[] Others);

[AkkaSerializable]
internal sealed record CrdtDotWire([property: AkkaField(1)] ReplicatorVersionEntryWire[] Entries);

[AkkaSerializable]
internal sealed record CrdtCountWire(
    [property: AkkaField(1)] ReplicatorUniqueAddressWire Node,
    [property: AkkaField(2)] ulong Value);

[AkkaSerializable]
internal sealed record CrdtORSetOperationWire(
    [property: AkkaField(1)] int Operation,
    [property: AkkaField(2)] CrdtORSetWire State);

[AkkaSerializable]
internal sealed record CrdtMapOperationsWire(
    [property: AkkaField(1)] CrdtTypeWire KeyType,
    [property: AkkaField(2)] CrdtTypeWire ValueType,
    [property: AkkaField(3)] CrdtMapOperationWire[] Entries);

[AkkaSerializable]
internal sealed record CrdtMapOperationWire(
    [property: AkkaField(1)] int Operation,
    [property: AkkaField(2)] CrdtORSetWire Underlying,
    [property: AkkaField(3)] CrdtElementsWire Keys,
    [property: AkkaField(4)] object?[] Values);
