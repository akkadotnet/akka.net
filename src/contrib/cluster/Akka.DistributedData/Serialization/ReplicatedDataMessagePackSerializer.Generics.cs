// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Serialization;
using Akka.Util;

namespace Akka.DistributedData.Serialization;

internal sealed partial class ReplicatedDataMessagePackSerializer
{
    private readonly ConcurrentDictionary<(string Operation, Type First, Type? Second), Func<object, object>> _genericFactories = new();
    private readonly ConcurrentDictionary<string, Type> _resolvedTypes = new(StringComparer.Ordinal);

    // Always test the switch before consulting a warm cache. Cached delegates are still dynamic
    // factories and must not become a way around the native boundary after bootstrapping.
    private object Dynamic(string operation, object value, Type first, Type? second = null)
    {
        if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            return InvokeDynamic(operation, value, first, second);
        throw DynamicError();
    }

    [RequiresDynamicCode("Arbitrary closed CRDT shapes require runtime generic instantiation.")]
    [RequiresUnreferencedCode("Arbitrary CRDT generic factories and payload types require reflection metadata.")]
    private object InvokeDynamic(string operation, object value, Type first, Type? second)
        => _genericFactories.GetOrAdd((operation, first, second), key =>
        {
            var method = typeof(ReplicatedDataMessagePackSerializer).GetMethod(key.Operation, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new SerializationException($"Missing CRDT factory [{key.Operation}].");
            var closed = method.MakeGenericMethod(key.Second is null ? [key.First] : [key.First, key.Second]);
            return (Func<object, object>)closed.CreateDelegate(typeof(Func<object, object>), this);
        })(value);

    private static SerializationException DynamicError() => new(
        "This generic replicated-data shape requires Akka.DynamicTypeLoading. No static native factory is available for it.");

    private static CrdtTypeWire Describe(Type type)
    {
        if (type == typeof(int)) return new CrdtTypeWire(0, null);
        if (type == typeof(long)) return new CrdtTypeWire(1, null);
        if (type == typeof(string)) return new CrdtTypeWire(2, null);
        if (type == typeof(IActorRef)) return new CrdtTypeWire(3, null);
        if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            return new CrdtTypeWire(4, type.TypeQualifiedName());
        throw DynamicError();
    }

    private Type Resolve(CrdtTypeWire type) => type.Kind switch
    {
        0 => typeof(int), 1 => typeof(long), 2 => typeof(string), 3 => typeof(IActorRef),
        4 => ResolveOther(type.Name),
        _ => throw new SerializationException($"Unknown replicated-data type kind [{type.Kind}].")
    };

    private Type ResolveOther(string? name)
    {
        if (AkkaFeatures.IsDynamicTypeLoadingSupported)
            return ResolveDynamicType(name);
        throw DynamicError();
    }

    [RequiresUnreferencedCode("Arbitrary CRDT payload types require reflection metadata.")]
    private Type ResolveDynamicType(string? name)
    {
        if (string.IsNullOrEmpty(name))
            throw new SerializationException("A replicated-data type name is required.");
        // Retain the legacy Sharding state alias without a dependency back to Sharding.
        if (name == "Akka.Cluster.Sharding.PersistentShardCoordinator+State, Akka.Cluster.Sharding")
            name = "Akka.Cluster.Sharding.ShardCoordinator+CoordinatorState, Akka.Cluster.Sharding";
        return _resolvedTypes.GetOrAdd(name, value => Type.GetType(value)
            ?? throw new SerializationException($"Cannot resolve replicated-data type [{value}]."));
    }

    private T[] Ordered<T>(IEnumerable<T> source)
    {
        if (typeof(T) == typeof(int)) return (T[])(object)((IEnumerable<int>)(object)source).OrderBy(value => value).ToArray();
        if (typeof(T) == typeof(long)) return (T[])(object)((IEnumerable<long>)(object)source).OrderBy(value => value).ToArray();
        if (typeof(T) == typeof(string)) return (T[])(object)((IEnumerable<string>)(object)source).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (typeof(T) == typeof(IActorRef)) return (T[])(object)((IEnumerable<IActorRef>)(object)source)
            .OrderBy(Akka.Serialization.Serialization.SerializedActorPath, StringComparer.Ordinal).ToArray();
        var entries = source.Select(value => (Value: value, Key: PayloadOrder(value))).ToArray();
        Array.Sort(entries, (left, right) => ComparePayload(left.Key, right.Key));
        return entries.Select(entry => entry.Value).ToArray();
    }

    private (int Id, string Manifest, byte[] Bytes) PayloadOrder(object? value)
    {
        var serializer = system.Serialization.FindSerializerFor(value);
        return (serializer.Identifier, Akka.Serialization.Serialization.ManifestFor(serializer, value), serializer.ToBinary(value));
    }

    private static int ComparePayload((int Id, string Manifest, byte[] Bytes) left, (int Id, string Manifest, byte[] Bytes) right)
    {
        var compare = left.Bytes.Length.CompareTo(right.Bytes.Length);
        if (compare != 0) return compare;
        compare = left.Bytes.AsSpan().SequenceCompareTo(right.Bytes);
        if (compare != 0) return compare;
        compare = left.Id.CompareTo(right.Id);
        return compare != 0 ? compare : StringComparer.Ordinal.Compare(left.Manifest, right.Manifest);
    }

    private static CrdtElementsWire Elements<T>(T[] values)
    {
        var type = Describe(typeof(T));
        return type.Kind switch
        {
            0 => new CrdtElementsWire(type, (int[])(object)values, [], [], [], []),
            1 => new CrdtElementsWire(type, [], (long[])(object)values, [], [], []),
            2 => new CrdtElementsWire(type, [], [], (string[])(object)values, [], []),
            3 => new CrdtElementsWire(type, [], [], [], (IActorRef?[])(object)values, []),
            _ => new CrdtElementsWire(type, [], [], [], [], values.Select(value => (object?)value).ToArray())
        };
    }

    private T[] Values<T>(CrdtElementsWire values)
    {
        if (Resolve(values.Type) != typeof(T))
            throw new SerializationException("Replicated-data element type does not match its containing type.");
        return values.Type.Kind switch
        {
            0 => (T[])(object)values.Ints,
            1 => (T[])(object)values.Longs,
            2 => (T[])(object)values.Strings,
            3 => (T[])(object)values.Refs.Select(Reference).ToArray(),
            _ => values.Others.Select(value => (T)value!).ToArray()
        };
    }

    private object WriteGSet<T>(object value) => new CrdtGSetWire(Elements(Ordered(((GSet<T>)value).Elements)));
    private object ReadGSet<T>(object value) => new GSet<T>(Values<T>(((CrdtGSetWire)value).Elements).ToImmutableHashSet());

    private object WriteORSet<T>(object value) where T : notnull
    {
        var set = (ORSet<T>)value;
        var keys = Ordered(set.ElementsMap.Keys);
        return new CrdtORSetWire(Elements(keys), ToVector(set.VersionVector),
            keys.Select(key => new CrdtDotWire(ToVector(set.ElementsMap[key]))).ToArray());
    }

    private object ReadORSet<T>(object value) where T : notnull
    {
        var set = (CrdtORSetWire)value;
        var keys = Values<T>(set.Elements);
        if (keys.Length != set.Dots.Length)
            throw new SerializationException("Replicated-data elements and causal dots must have equal counts.");
        return new ORSet<T>(keys.Select((key, index) => (key, vector: FromVector(set.Dots[index].Entries)))
            .ToImmutableDictionary(entry => entry.key, entry => entry.vector), FromVector(set.Vector));
    }

    private object WriteRegister<T>(object value)
    {
        var register = (LWWRegister<T>)value;
        return new CrdtRegisterWire(Describe(typeof(T)), register.Value, ToNode(register.UpdatedBy), register.Timestamp);
    }
    private object ReadRegister<T>(object value)
    {
        var register = (CrdtRegisterWire)value;
        return new LWWRegister<T>(FromNode(register.UpdatedBy), (T)register.Value!, register.Timestamp);
    }

    private object ReadSetAdd<T>(object value) where T : notnull => new ORSet<T>.AddDeltaOperation((ORSet<T>)ReadORSet<T>(value));
    private object ReadSetRemove<T>(object value) where T : notnull => new ORSet<T>.RemoveDeltaOperation((ORSet<T>)ReadORSet<T>(value));
    private object ReadSetFull<T>(object value) where T : notnull => new ORSet<T>.FullStateDeltaOperation((ORSet<T>)ReadORSet<T>(value));

    private CrdtORSetGroupWire ToSetGroup(ORSet.IDeltaGroupOperation group)
    {
        var operations = group.OperationsSerialization.Select(operation => operation switch
        {
            ORSet.IAddDeltaOperation add => new CrdtORSetOperationWire(0, ToORSet(add.UnderlyingSerialization)),
            ORSet.IRemoveDeltaOperation remove => new CrdtORSetOperationWire(1, ToORSet(remove.UnderlyingSerialization)),
            ORSet.IFullStateDeltaOperation full => new CrdtORSetOperationWire(2, ToORSet(full.UnderlyingSerialization)),
            _ => throw new SerializationException("Nested or unknown ORSet delta operation.")
        }).ToArray();
        if (operations.Length == 0)
            throw new SerializationException("An ORSet delta group requires at least one operation.");
        return new CrdtORSetGroupWire(operations[0].State.Elements.Type, operations);
    }
    private object ReadSetGroup<T>(object value) where T : notnull
    {
        var group = (CrdtORSetGroupWire)value;
        return new ORSet<T>.DeltaGroup(group.Operations.Select(operation => operation.Operation switch
        {
            0 => (IReplicatedData)ReadSetAdd<T>(operation.State),
            1 => (IReplicatedData)ReadSetRemove<T>(operation.State),
            2 => (IReplicatedData)ReadSetFull<T>(operation.State),
            _ => throw new SerializationException($"Unknown ORSet delta operation [{operation.Operation}].")
        }).ToImmutableArray());
    }
}
