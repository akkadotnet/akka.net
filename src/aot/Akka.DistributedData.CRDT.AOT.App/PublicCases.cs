// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
#nullable enable
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Serialization;
using UniqueAddress = Akka.Cluster.UniqueAddress;

namespace Akka.DistributedData.CRDT.AOT.App;

internal sealed record PublicCase(string Name, string Manifest, object Value, Action<object> Check);

internal static class PublicCases
{
    internal const int Count = 69;

    internal static List<PublicCase> Create(ExtendedActorSystem system, Serializer legacy)
    {
        var result = new List<PublicCase>();
        var owner = Owner(system);
        var observer = new UniqueAddress(new Address(system.Provider.DefaultAddress.Protocol, system.Name,
            "observer.example", 2553), long.MinValue);
        var vector = VersionVector.Create(owner, 17);
        Known(int.MinValue, "Int", long.MinValue);
        Known(long.MinValue, "Long", long.MaxValue);
        Known("value", "String", 123456789);
        Known<IActorRef>(system.DeadLetters, "RefLocal", 123456789);
        Add("GSetRefNoSender", "B", GSet<IActorRef>.Empty.Add(ActorRefs.NoSender!), actual =>
        {
            var set = (GSet<IActorRef>)actual;
            Proof.Require(set.Count == 1 && set.Elements.Single().Equals(system.Provider.ResolveActorRef(string.Empty)),
                "Provider-dependent NoSender set read");
        });
        Add("RegisterNull", "E", new LWWRegister<string?>(owner, null, 123456789));
        Add("Counter", "F", GCounter.Empty.Increment(owner, 7));
        Add("CounterEmpty", "F", GCounter.Empty);
        Add("CounterUnsignedMax", "F", GCounter.Empty.Increment(owner, ulong.MaxValue));
        Add("CounterMulti", "F", GCounter.Empty.Increment(owner, ulong.MaxValue).Increment(observer, 11));
        Add("PNCounter", "G", PNCounter.Empty.Increment(owner, 9).Decrement(owner, 2));
        Add("PNCounterEmpty", "G", PNCounter.Empty);
        Add("PNCounterMulti", "G", PNCounter.Empty.Increment(owner, long.MaxValue).Decrement(observer, 29));
        Add("FlagTrue", "D", Flag.True);
        Add("FlagFalse", "D", Flag.False);
        Add("CounterKey", "f", new GCounterKey("key"));
        Add("PNCounterKey", "g", new PNCounterKey("key"));
        Add("FlagKey", "d", new FlagKey("key"));
        Add("VersionVector", "L", vector);
        Add("VersionVectorEmpty", "L", VersionVector.Empty);
        var multiVector = VersionVector.Create(ImmutableDictionary<UniqueAddress, long>.Empty
            .Add(owner, 17).Add(observer, 33));
        Add("VersionVectorMulti", "L", multiVector);
        Add("GSetMulti", "B", GSet.Create("one", "two"));
        Add("ORSetMulti", "C", new ORSet<string>(ImmutableDictionary<string, VersionVector>.Empty
            .Add("one", vector).Add("two", VersionVector.Create(observer, 3)), multiVector));

        // Public legacy reads are the bootstrap for controls whose concrete classes are internal.
        // No private names, reflection or friend access are used by this consumer.
        var deleted = legacy.FromBinary(Array.Empty<byte>(), "A");
        Add("Deleted", "A", deleted, actual => Proof.Require(ReferenceEquals(actual, deleted), "Deleted singleton identity"));
        var addRecord = FrozenRecords.All.Single(item => !item.Generic && item.Name == "ORSetAdd");
        var add = legacy.FromBinary(Convert.FromHexString(addRecord.Hex), "Ca");
        var expectedAddState = new ORSet<string>(ImmutableDictionary<string, VersionVector>.Empty.Add("key", vector), vector);
        Proof.Require(add is IReplicatedDelta delta && expectedAddState.Equals(delta.Zero.MergeDelta(delta)),
            "Frozen add bootstrap contains the complete fixed causal state");
        Add("ORSetAdd", "Ca", add);
        Proof.Require(result.Count == Count, "Complete public corpus");
        return result;

        void Known<T>(T value, string name, long timestamp) where T : notnull
        {
            Add("GSet" + name, "B", GSet.Create(value));
            var set = new ORSet<T>(ImmutableDictionary<T, VersionVector>.Empty.Add(value, vector), vector);
            Add("ORSet" + name, "C", set);
            Add("Register" + name, "E", new LWWRegister<T>(owner, value, timestamp));
            Add("GSetKey" + name, "b", new GSetKey<T>("key"));
            Add("ORSetKey" + name, "c", new ORSetKey<T>("key"));
            Add("RegisterKey" + name, "e", new LWWRegisterKey<T>("key"));
            Add("PNCounterDictionaryKey" + name, "j", new PNCounterDictionaryKey<T>("key"));
            Add("GSetEmpty" + name, "B", GSet<T>.Empty);
            Add("ORSetEmpty" + name, "C", ORSet<T>.Empty);
            var removed = set.Remove(owner, value).Delta;
            var full = set.Clear(owner).Delta;
            Add("RemoveDelta" + name, "Cr", removed);
            Add("FullDelta" + name, "Cf", full);
            Add("GroupDelta" + name, "Cg", removed.Merge(full));
        }

        void Add(string name, string manifest, object value, Action<object>? check = null)
            => result.Add(new PublicCase(name, manifest, value, actual =>
            {
                Proof.Require(actual.GetType() == value.GetType(), "Exact public type: " + name);
                if (check is not null)
                    check(actual);
                else
                    Proof.Require(value.Equals(actual), "Complete domain state: " + name);
                if (check is null && value is IReplicatedDelta expectedDelta && actual is IReplicatedDelta actualDelta)
                    Proof.Require(expectedDelta.Zero.MergeDelta(expectedDelta).Equals(actualDelta.Zero.MergeDelta(actualDelta)),
                        "Delta application state: " + name);
            }));
    }

    internal static UniqueAddress Owner(ExtendedActorSystem system) => new(new Address(
        system.Provider.DefaultAddress.Protocol, system.Name, "counter.example", 2552), 1L << 40);

    internal static void CheckHistorical(FrozenRecord record, object value, IReadOnlyList<PublicCase> cases, Serializer legacy, ExtendedActorSystem system)
    {
        var name = record.Name switch
        {
            "GSetEmpty" => "GSetEmptyString", "ORSetEmpty" => "ORSetEmptyString",
            "GSetRefEmpty" => "GSetEmptyRefLocal", _ => record.Name
        };
        var match = cases.SingleOrDefault(item => item.Name == name);
        if (match is not null)
            match.Check(value);
        else if (record.Manifest is "Cr" or "Cf" or "Cg")
        {
            Proof.Require(value is IReplicatedDelta, "Historical ORSet delta public contract: " + name);
            Proof.Require(legacy.ToBinary(value).AsSpan().SequenceEqual(Convert.FromHexString(record.Hex)),
                "All historical delta fields remain byte exact: " + name);
            var state = ((IReplicatedDelta)value).Zero.MergeDelta((IReplicatedDelta)value);
            var vector = VersionVector.Create(Owner(system), 17);
            var expected = record.Manifest == "Cr" ? ORSet<string>.Empty : new ORSet<string>(
                ImmutableDictionary<string, VersionVector>.Empty.Add("key", vector), vector);
            Proof.Require(expected.Equals(state), "Complete historical delta application state: " + name);
        }
        else
            throw new InvalidOperationException("Historical record has no complete assertion: " + name);
    }
}
