// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;

namespace Akka.DistributedData.Serialization;

internal sealed partial class ReplicatedDataMessagePackSerializer
{
    private object WriteDictionary<TKey, TValue>(object value) where TKey : notnull where TValue : IReplicatedData<TValue>
    {
        var map = (ORDictionary<TKey, TValue>)value;
        var keys = Ordered(map.ValueMap.Keys);
        return new CrdtDictionaryWire((CrdtORSetWire)WriteORSet<TKey>(map.KeySet), Elements(keys), Describe(typeof(TValue)),
            keys.Select(key => (object?)map.ValueMap[key]).ToArray());
    }
    private object ReadDictionary<TKey, TValue>(object value) where TKey : notnull where TValue : IReplicatedData<TValue>
    {
        var map = (CrdtDictionaryWire)value;
        return new ORDictionary<TKey, TValue>((ORSet<TKey>)ReadORSet<TKey>(map.KeySet),
            Pairs<TKey, TValue, object?>(map.EntryKeys, map.Values, item => (TValue)item!));
    }

    private object WriteLWWDictionary<TKey, TValue>(object value) where TKey : notnull
    {
        var map = ((LWWDictionary<TKey, TValue>)value).Underlying;
        var keys = Ordered(map.ValueMap.Keys);
        return new CrdtLWWDictionaryWire((CrdtORSetWire)WriteORSet<TKey>(map.KeySet), Elements(keys), Describe(typeof(TValue)),
            keys.Select(key => (CrdtRegisterWire)WriteRegister<TValue>(map.ValueMap[key])).ToArray());
    }
    private object ReadLWWDictionary<TKey, TValue>(object value) where TKey : notnull
    {
        var map = (CrdtLWWDictionaryWire)value;
        return new LWWDictionary<TKey, TValue>(new ORDictionary<TKey, LWWRegister<TValue>>((ORSet<TKey>)ReadORSet<TKey>(map.KeySet),
            Pairs<TKey, LWWRegister<TValue>, CrdtRegisterWire>(map.EntryKeys, map.Values, item => (LWWRegister<TValue>)ReadRegister<TValue>(item))));
    }

    private object WritePNDictionary<TKey>(object value) where TKey : notnull
    {
        var map = ((PNCounterDictionary<TKey>)value).Underlying;
        var keys = Ordered(map.ValueMap.Keys);
        return new CrdtPNDictionaryWire((CrdtORSetWire)WriteORSet<TKey>(map.KeySet), Elements(keys),
            keys.Select(key => new CrdtPNCounterWire(ToCounter(map.ValueMap[key].Increments), ToCounter(map.ValueMap[key].Decrements))).ToArray());
    }
    private object ReadPNDictionary<TKey>(object value) where TKey : notnull
    {
        var map = (CrdtPNDictionaryWire)value;
        return new PNCounterDictionary<TKey>(new ORDictionary<TKey, PNCounter>((ORSet<TKey>)ReadORSet<TKey>(map.KeySet),
            Pairs<TKey, PNCounter, CrdtPNCounterWire>(map.EntryKeys, map.Values,
                item => new PNCounter(FromCounter(item.Increments), FromCounter(item.Decrements)))));
    }

    private object WriteMultiDictionary<TKey, TValue>(object value) where TKey : notnull where TValue : notnull
    {
        var multi = (ORMultiValueDictionary<TKey, TValue>)value;
        var map = multi.Underlying;
        var keys = Ordered(map.ValueMap.Keys);
        return new CrdtMultiDictionaryWire((CrdtORSetWire)WriteORSet<TKey>(map.KeySet), Elements(keys), Describe(typeof(TValue)),
            keys.Select(key => (CrdtORSetWire)WriteORSet<TValue>(map.ValueMap[key])).ToArray(), multi.DeltaValues);
    }
    private object ReadMultiDictionary<TKey, TValue>(object value) where TKey : notnull where TValue : notnull
    {
        var map = (CrdtMultiDictionaryWire)value;
        return new ORMultiValueDictionary<TKey, TValue>(new ORDictionary<TKey, ORSet<TValue>>((ORSet<TKey>)ReadORSet<TKey>(map.KeySet),
            Pairs<TKey, ORSet<TValue>, CrdtORSetWire>(map.EntryKeys, map.Values, item => (ORSet<TValue>)ReadORSet<TValue>(item))), map.WithValueDeltas);
    }

    private ImmutableDictionary<TKey, TValue> Pairs<TKey, TValue, TWire>(CrdtElementsWire entries, TWire[] values, Func<TWire, TValue> read) where TKey : notnull
    {
        var keys = Values<TKey>(entries);
        if (keys.Length != values.Length)
            throw new SerializationException("Replicated-data entry keys and values must have equal counts.");
        return keys.Select((key, index) => (key, value: read(values[index]))).ToImmutableDictionary(entry => entry.key, entry => entry.value);
    }

    private CrdtMapOperationsWire ToMapOperations(ORDictionary.IDeltaOperation operation)
        => (CrdtMapOperationsWire)Dynamic(nameof(WriteMapOperations), operation, operation.KeyType, operation.ValueType);

    private object WriteMapOperations<TKey, TValue>(object value) where TKey : notnull where TValue : IReplicatedData<TValue>
    {
        var operation = (ORDictionary<TKey, TValue>.IDeltaOperation)value;
        var operations = operation is ORDictionary<TKey, TValue>.DeltaGroup group ? group.Operations : [operation];
        if (operations.Length == 0)
            throw new SerializationException("A dictionary delta group requires at least one operation.");
        return new CrdtMapOperationsWire(Describe(typeof(TKey)), Describe(typeof(TValue)), operations.Select(Entry).ToArray());

        CrdtMapOperationWire Entry(ORDictionary<TKey, TValue>.IDeltaOperation op)
        {
            return op switch
            {
                ORDictionary<TKey, TValue>.PutDeltaOperation put => new CrdtMapOperationWire(0, Underlying(put.Underlying),
                    Elements(new[] { put.Key }), [put.Value]),
                ORDictionary<TKey, TValue>.RemoveDeltaOperation remove => new CrdtMapOperationWire(1, Underlying(remove.Underlying),
                    Elements(Array.Empty<TKey>()), []),
                ORDictionary<TKey, TValue>.RemoveKeyDeltaOperation remove => new CrdtMapOperationWire(2, Underlying(remove.Underlying),
                    Elements(new[] { remove.Key }), []),
                ORDictionary<TKey, TValue>.UpdateDeltaOperation update => Update(update),
                _ => throw new SerializationException($"Unknown dictionary delta [{op.GetType()}].")
            };
        }
        CrdtORSetWire Underlying(ORSet<TKey>.IDeltaOperation op) => ToORSet(((ORSet.IDeltaOperation)op).UnderlyingSerialization);
        CrdtMapOperationWire Update(ORDictionary<TKey, TValue>.UpdateDeltaOperation update)
        {
            var keys = Ordered(update.Values.Keys);
            return new CrdtMapOperationWire(3, Underlying(update.Underlying), Elements(keys), keys.Select(key => (object?)update.Values[key]).ToArray());
        }
    }

    private object ReadMapOperations(CrdtMapOperationsWire operations, int kind)
        => Dynamic(nameof(ReadMapDelta), (operations, kind), Resolve(operations.KeyType), Resolve(operations.ValueType));

    private object ReadMapDelta<TKey, TValue>(object value) where TKey : notnull where TValue : IReplicatedData<TValue>
    {
        var (wire, kind) = ((CrdtMapOperationsWire, int))value;
        var entries = wire.Entries.Select(ReadEntry).ToArray();
        return kind switch
        {
            0 => (ORDictionary.IPutDeltaOp)entries[0],
            1 => (ORDictionary.IRemoveDeltaOp)entries[0],
            2 => (ORDictionary.IRemoveKeyDeltaOp)entries[0],
            3 => (ORDictionary.IUpdateDeltaOp)entries[0],
            4 => new ORDictionary<TKey, TValue>.DeltaGroup(entries),
            _ => throw new SerializationException("Unknown dictionary manifest kind.")
        };

        ORDictionary<TKey, TValue>.IDeltaOperation ReadEntry(CrdtMapOperationWire entry)
        {
            var state = (ORSet<TKey>)ReadORSet<TKey>(entry.Underlying);
            return entry.Operation switch
            {
                0 => new ORDictionary<TKey, TValue>.PutDeltaOperation(new ORSet<TKey>.AddDeltaOperation(state), OneKey(entry.Keys), (TValue)OneValue(entry.Values)!),
                1 => new ORDictionary<TKey, TValue>.RemoveDeltaOperation(new ORSet<TKey>.RemoveDeltaOperation(state)),
                2 => new ORDictionary<TKey, TValue>.RemoveKeyDeltaOperation(new ORSet<TKey>.RemoveDeltaOperation(state), OneKey(entry.Keys)),
                3 => new ORDictionary<TKey, TValue>.UpdateDeltaOperation(new ORSet<TKey>.AddDeltaOperation(state),
                    Pairs<TKey, IReplicatedData, object?>(entry.Keys, entry.Values, item => (IReplicatedData)item!)),
                _ => throw new SerializationException($"Unknown dictionary delta operation [{entry.Operation}].")
            };
        }
        TKey OneKey(CrdtElementsWire keys)
        {
            var values = Values<TKey>(keys);
            if (values.Length != 1)
                throw new SerializationException("This dictionary delta requires exactly one key.");
            return values[0];
        }
        static object? OneValue(object?[] values)
        {
            if (values.Length != 1)
                throw new SerializationException("A dictionary put requires exactly one value.");
            return values[0];
        }
    }

    private object ReadMapWrapper(CrdtMapOperationsWire operations, string kind, bool withValueDeltas)
    {
        // No custom generic registry is introduced: these wrappers remain explicitly dynamic.
        var key = Resolve(operations.KeyType);
        if (kind == "pn")
            return Dynamic(nameof(ReadPNDelta), operations, key);
        var value = Resolve(operations.ValueType);
        if (!value.IsGenericType || value.GenericTypeArguments.Length != 1)
            throw new SerializationException("A wrapped dictionary delta requires its declared generic value type.");
        return kind == "lww"
            ? Dynamic(nameof(ReadLWWDelta), operations, key, value.GenericTypeArguments[0])
            : Dynamic(nameof(ReadMultiDelta), (operations, withValueDeltas), key, value.GenericTypeArguments[0]);
    }

    private object ReadPNDelta<TKey>(object value) where TKey : notnull => new PNCounterDictionary<TKey>.PNCounterDictionaryDelta(
        (ORDictionary<TKey, PNCounter>.IDeltaOperation)ReadMapDelta<TKey, PNCounter>(((CrdtMapOperationsWire)value, 4)));
    private object ReadLWWDelta<TKey, TValue>(object value) where TKey : notnull => new LWWDictionary<TKey, TValue>.LWWDictionaryDelta(
        (ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation)ReadMapDelta<TKey, LWWRegister<TValue>>(((CrdtMapOperationsWire)value, 4)));
    private object ReadMultiDelta<TKey, TValue>(object value) where TKey : notnull where TValue : notnull
    {
        var (wire, deltas) = ((CrdtMapOperationsWire, bool))value;
        return new ORMultiValueDictionary<TKey, TValue>.ORMultiValueDictionaryDelta(
            (ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation)ReadMapDelta<TKey, ORSet<TValue>>((wire, 4)), deltas);
    }
}
