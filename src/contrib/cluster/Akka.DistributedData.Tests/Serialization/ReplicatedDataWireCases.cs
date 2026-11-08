// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.DistributedData.Internal;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.Tests.Serialization;

/// <summary>Fixed logical values; never use public ORSet/ORDictionary mutation clocks in wire fixtures.</summary>
internal static class ReplicatedDataWireCases
{
    internal const string ReferencePath = "akka.tcp://crdt-wire@127.0.0.1:2552/user/ref#42";

    internal static IReadOnlyList<(object Message, string Manifest, string Name)> Create(string systemName, Func<string, IActorRef> resolve, string protocol = "akka.tcp")
    {
        var node = new UniqueAddress(new Address(protocol, systemName, "counter.example", 2552), 1L << 40);
        var vector = VersionVector.Create(node, 17);
        var counter = GCounter.Empty.Increment(node, 7);
        var pnCounter = PNCounter.Empty.Increment(node, 9).Decrement(node, 2);
        var register = new LWWRegister<string>(node, "value", 123456789);
        var keys = Set("key");
        var added = new ORSet<string>.AddDeltaOperation(keys);
        var removed = new ORSet<string>.RemoveDeltaOperation(new ORSet<string>(keys.ElementsMap, VersionVector.Empty));
        var full = new ORSet<string>.FullStateDeltaOperation(keys);
        var put = new ORDictionary<string, GCounter>.PutDeltaOperation(added, "key", counter);
        var remove = new ORDictionary<string, GCounter>.RemoveDeltaOperation(removed);
        var removeKey = new ORDictionary<string, GCounter>.RemoveKeyDeltaOperation(removed, "key");
        var update = new ORDictionary<string, GCounter>.UpdateDeltaOperation(added,
            ImmutableDictionary<string, IReplicatedData>.Empty.Add("key", counter));
        var lwwGroup = new ORDictionary<string, LWWRegister<string>>.DeltaGroup(
            new[] { new ORDictionary<string, LWWRegister<string>>.PutDeltaOperation(added, "key", register) });
        var pnGroup = new ORDictionary<string, PNCounter>.DeltaGroup(
            new[] { new ORDictionary<string, PNCounter>.PutDeltaOperation(added, "key", pnCounter) });
        var multiGroup = new ORDictionary<string, ORSet<string>>.DeltaGroup(
            new[] { new ORDictionary<string, ORSet<string>>.PutDeltaOperation(added, "key", Set("item")) });
        var reference = resolve(ReferencePath);

        return new List<(object, string, string)>
        {
            (DeletedData.Instance, "A", "Deleted"),
            (GSet.Create("value"), "B", "GSetString"),
            (Set("value"), "C", "ORSetString"),
            (Flag.True, "D", "FlagTrue"),
            (register, "E", "RegisterString"),
            (counter, "F", "Counter"),
            (pnCounter, "G", "PNCounter"),
            (Map(counter), "H", "Dictionary"),
            (new LWWDictionary<string, string>(Map(register)), "I", "LWWDictionary"),
            (new PNCounterDictionary<string>(Map(pnCounter)), "J", "PNCounterDictionary"),
            (new ORMultiValueDictionary<string, string>(Map(Set("item")), false), "K", "MultiDictionary"),
            (vector, "L", "VersionVector"),
            (new GSetKey<string>("key"), "b", "GSetKey"),
            (new ORSetKey<string>("key"), "c", "ORSetKey"),
            (new FlagKey("key"), "d", "FlagKey"),
            (new LWWRegisterKey<string>("key"), "e", "RegisterKey"),
            (new GCounterKey("key"), "f", "CounterKey"),
            (new PNCounterKey("key"), "g", "PNCounterKey"),
            (new ORDictionaryKey<string, GCounter>("key"), "h", "DictionaryKey"),
            (new LWWDictionaryKey<string, string>("key"), "i", "LWWDictionaryKey"),
            (new PNCounterDictionaryKey<string>("key"), "j", "PNCounterDictionaryKey"),
            (new ORMultiValueDictionaryKey<string, string>("key"), "k", "MultiDictionaryKey"),
            (added, "Ca", "ORSetAdd"),
            (removed, "Cr", "ORSetRemove"),
            (full, "Cf", "ORSetFull"),
            (new ORSet<string>.DeltaGroup(ImmutableArray.Create<IReplicatedData>(added, removed, full)), "Cg", "ORSetGroup"),
            (put, "Ha", "DictionaryPut"),
            (remove, "Hr", "DictionaryRemove"),
            (removeKey, "Hk", "DictionaryRemoveKey"),
            (update, "Hu", "DictionaryUpdate"),
            (new ORDictionary<string, GCounter>.DeltaGroup(new ORDictionary<string, GCounter>.IDeltaOperation[] { put, removeKey }), "Hg", "DictionaryGroup"),
            (new LWWDictionary<string, string>.LWWDictionaryDelta(lwwGroup), "Ig", "LWWDictionaryDelta"),
            (new PNCounterDictionary<string>.PNCounterDictionaryDelta(pnGroup), "Jo", "PNCounterDictionaryDelta"),
            (new ORMultiValueDictionary<string, string>.ORMultiValueDictionaryDelta(multiGroup, false), "Ko", "MultiDictionaryDelta"),

            (GSet<string>.Empty, "B", "GSetEmpty"),
            (ORSet<string>.Empty, "C", "ORSetEmpty"),
            (Flag.False, "D", "FlagFalse"),
            (GCounter.Empty, "F", "CounterEmpty"),
            (PNCounter.Empty, "G", "PNCounterEmpty"),
            (ORDictionary<string, GCounter>.Empty, "H", "DictionaryEmpty"),
            (LWWDictionary<string, string>.Empty, "I", "LWWDictionaryEmpty"),
            (PNCounterDictionary<string>.Empty, "J", "PNCounterDictionaryEmpty"),
            (ORMultiValueDictionary<string, string>.Empty, "K", "MultiDictionaryEmpty"),
            (VersionVector.Empty, "L", "VersionVectorEmpty"),
            (GCounter.Empty.Increment(node, ulong.MaxValue), "F", "CounterUnsignedMax"),
            (GSet.Create(int.MinValue), "B", "GSetInt"),
            (GSet.Create(long.MinValue), "B", "GSetLong"),
            (GSet.Create<IActorRef>(reference), "B", "GSetRef"),
            (GSet<IActorRef>.Empty, "B", "GSetRefEmpty"),
            (GSet<IActorRef>.Empty.Add(ActorRefs.NoSender!), "B", "GSetRefNoSender"),
            (GSet.Create<object>("value"), "B", "GSetOther"),
            (GSet<object?>.Empty.Add(null), "B", "GSetNull"),
            (Set(int.MinValue), "C", "ORSetInt"),
            (Set(long.MinValue), "C", "ORSetLong"),
            (Set(reference), "C", "ORSetRef"),
            (Set<object>("value"), "C", "ORSetOther"),
            (new LWWRegister<int>(node, int.MinValue, long.MinValue), "E", "RegisterInt"),
            (new LWWRegister<long>(node, long.MinValue, long.MaxValue), "E", "RegisterLong"),
            (new LWWRegister<IActorRef>(node, reference, 123456789), "E", "RegisterRef"),
            (new LWWRegister<string?>(node, null, 123456789), "E", "RegisterNull"),
            (new LWWRegister<GCounter>(node, counter, 123456789), "E", "RegisterNested"),
            (new ORMultiValueDictionary<string, string>(Map(Set("item")), true), "K", "MultiDictionaryValueDeltas"),
            (new ORMultiValueDictionary<string, string>.ORMultiValueDictionaryDelta(multiGroup, true), "Ko", "MultiDictionaryDeltaValueDeltas")
        };

        ORSet<T> Set<T>(T value) where T : notnull => new(ImmutableDictionary<T, VersionVector>.Empty.Add(value, vector), vector);
        ORDictionary<string, T> Map<T>(T value) where T : IReplicatedData<T> => new(keys,
            ImmutableDictionary<string, T>.Empty.Add("key", value));
    }
}
