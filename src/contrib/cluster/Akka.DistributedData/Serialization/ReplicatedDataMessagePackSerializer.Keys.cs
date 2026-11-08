// -----------------------------------------------------------------------
// Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Util;

namespace Akka.DistributedData.Serialization;

internal sealed partial class ReplicatedDataMessagePackSerializer
{
    private object ToKey(IKey key) => key switch
    {
        GCounterKey => new CrdtCounterKeyWire(key.Id),
        PNCounterKey => new CrdtPNCounterKeyWire(key.Id),
        FlagKey => new CrdtFlagKeyWire(key.Id),
        IGSetKey typed => new CrdtGSetKeyWire(key.Id, Describe(typed.SetType)),
        IORSetKey typed => new CrdtORSetKeyWire(key.Id, Describe(typed.SetType)),
        ILWWRegisterKey typed => new CrdtRegisterKeyWire(key.Id, Describe(typed.RegisterType)),
        IPNCounterDictionaryKey typed => new CrdtPNDictionaryKeyWire(key.Id, Describe(typed.KeyType)),
        IORDictionaryKey typed => ToMapKey("h", key.Id, typed.KeyType, typed.ValueType),
        ILWWDictionaryKey typed => ToMapKey("i", key.Id, typed.KeyType, typed.ValueType),
        IORMultiValueDictionaryKey typed => ToMapKey("k", key.Id, typed.KeyType, typed.ValueType),
        _ => throw new ArgumentException($"Unsupported replicated-data key [{key.GetType()}].", nameof(key))
    };

    private static object ToMapKey(string kind, string id, Type key, Type value)
    {
        if (!AkkaFeatures.IsDynamicTypeLoadingSupported)
            throw DynamicError();
        return kind switch
        {
            "h" => new CrdtDictionaryKeyWire(id, Describe(key), Describe(value)),
            "i" => new CrdtLWWDictionaryKeyWire(id, Describe(key), Describe(value)),
            "k" => new CrdtMultiDictionaryKeyWire(id, Describe(key), Describe(value)),
            _ => throw new SerializationException("Unknown replicated-data map key kind.")
        };
    }

    private object FromKey(object key) => key switch
    {
        CrdtGSetKeyWire typed => ReadSimpleKey("b", typed.Id, typed.Type),
        CrdtORSetKeyWire typed => ReadSimpleKey("c", typed.Id, typed.Type),
        CrdtRegisterKeyWire typed => ReadSimpleKey("e", typed.Id, typed.Type),
        CrdtPNDictionaryKeyWire typed => ReadSimpleKey("j", typed.Id, typed.Type),
        CrdtDictionaryKeyWire typed => Dynamic(nameof(ReadDictionaryKey), typed.Id, Resolve(typed.KeyType), Resolve(typed.ValueType)),
        CrdtLWWDictionaryKeyWire typed => Dynamic(nameof(ReadLWWDictionaryKey), typed.Id, Resolve(typed.KeyType), Resolve(typed.ValueType)),
        CrdtMultiDictionaryKeyWire typed => Dynamic(nameof(ReadMultiDictionaryKey), typed.Id, Resolve(typed.KeyType), Resolve(typed.ValueType)),
        _ => throw new SerializationException($"Unsupported replicated-data wire message [{key.GetType()}].")
    };

    private object ReadSimpleKey(string kind, string id, CrdtTypeWire type) => (kind, type.Kind) switch
    {
        ("b", 0) => new GSetKey<int>(id), ("b", 1) => new GSetKey<long>(id),
        ("b", 2) => new GSetKey<string>(id), ("b", 3) => new GSetKey<IActorRef>(id),
        ("c", 0) => new ORSetKey<int>(id), ("c", 1) => new ORSetKey<long>(id),
        ("c", 2) => new ORSetKey<string>(id), ("c", 3) => new ORSetKey<IActorRef>(id),
        ("e", 0) => new LWWRegisterKey<int>(id), ("e", 1) => new LWWRegisterKey<long>(id),
        ("e", 2) => new LWWRegisterKey<string>(id), ("e", 3) => new LWWRegisterKey<IActorRef>(id),
        ("j", 0) => new PNCounterDictionaryKey<int>(id), ("j", 1) => new PNCounterDictionaryKey<long>(id),
        ("j", 2) => new PNCounterDictionaryKey<string>(id), ("j", 3) => new PNCounterDictionaryKey<IActorRef>(id),
        _ => Dynamic(nameof(ReadGenericKey), (kind, id), Resolve(type))
    };

    private object ReadGenericKey<T>(object value)
    {
        var (kind, id) = ((string, string))value;
        return kind switch
        {
            "b" => new GSetKey<T>(id), "c" => new ORSetKey<T>(id),
            "e" => new LWWRegisterKey<T>(id), "j" => new PNCounterDictionaryKey<T>(id),
            _ => throw new SerializationException("Unknown replicated-data key kind.")
        };
    }
    private object ReadDictionaryKey<TKey, TValue>(object value) where TValue : IReplicatedData<TValue> => new ORDictionaryKey<TKey, TValue>((string)value);
    private object ReadLWWDictionaryKey<TKey, TValue>(object value) => new LWWDictionaryKey<TKey, TValue>((string)value);
    private object ReadMultiDictionaryKey<TKey, TValue>(object value) => new ORMultiValueDictionaryKey<TKey, TValue>((string)value);
}
