// //-----------------------------------------------------------------------
// // <copyright file="ReplicatedDataSerializer.GenericCache.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using Akka.DistributedData.Serialization.Proto.Msg;
using Google.Protobuf.Collections;

namespace Akka.DistributedData.Serialization;

public sealed partial class ReplicatedDataSerializer
{
    private sealed class LazyPool<T>
    {
        private readonly ConcurrentQueue<T> _poolItems = new();
        private int _numItems = new();
        private readonly int _maxItems;
        public LazyPool(int maxItems)
        {
            _maxItems = maxItems;
        }

        public bool TryGetValue(out T value)
        {
            if (_numItems > 0)
            {
                if (Interlocked.Decrement(ref _numItems) < 0)
                {
                    Interlocked.Increment(ref _numItems);
                }
                else
                {
                    return _poolItems.TryDequeue(out value);
                }
            }
            value = default;
            return false;
        }

        public bool TryReturn(T value)
        {
            if (_numItems < _maxItems)
            {
                if (Interlocked.Increment(ref _numItems) < (_maxItems * 2))
                {
                    _poolItems.Enqueue(value);
                    return true;
                }
                else
                {
                    Interlocked.Decrement(ref _numItems);
                }
            }

            return false;
        }
    }
    private static class SerDeserGenericCache
    {
        private readonly struct TwoTypeLookup : IEquatable<TwoTypeLookup>
        {
            public readonly Type Item1;
            public readonly Type Item2;

            public TwoTypeLookup(Type item1, Type item2)
            {
                Item1 = item1;
                Item2 = item2;
            }

            public override bool Equals(object obj)
            {
                return obj is TwoTypeLookup other && Equals(other);
            }

            public bool Equals(TwoTypeLookup other)
            {
                return Item1 == other.Item1 && Item2 == other.Item2;
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Item1.GetHashCode() * 397) ^ Item2.GetHashCode();
                }
            }

            public static bool operator ==(TwoTypeLookup left, TwoTypeLookup right)
            {
                return left.Equals(right);
            }

            public static bool operator !=(TwoTypeLookup left, TwoTypeLookup right)
            {
                return !left.Equals(right);
            }
        }
        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer,Proto.Msg.ORSet, VersionVector, IORSet>>
            toGenericOrSetCache = new();

        public static IORSet ToGenericORSet(Type k,
            ReplicatedDataSerializer serializer,
            Proto.Msg.ORSet set, 
            VersionVector versionVectors)
        {
            if (toGenericOrSetCache.TryGetValue(k,
                    out var value) ==
                false)
            {
                value = toGenericOrSetCache.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericTwoArgs<
                                Proto.Msg.ORSet, VersionVector, IORSet>(
                                ORSetMaker, kc,true)
                            .Compile());
            }

            return value(serializer, set, versionVectors);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, IORSet, Proto.Msg.ORSet,
                    Proto.Msg.ORSet>>
            orSetUnknownToProto = new();

        public static Proto.Msg.ORSet ORSetUnknownToProto(Type k,
            ReplicatedDataSerializer serializer,
            IORSet set, Proto.Msg.ORSet b)
        {
            
               var value = orSetUnknownToProto.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericTwoArgs<
                                IORSet, Proto.Msg.ORSet, Proto.Msg.ORSet>(
                                ORSetUnknownMaker, kc)
                            .Compile());
            return value(serializer, set, b);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ImmutableArray<IReplicatedData>,
                    ORSet.IDeltaGroupOperation>>
            orDeltaGroupCtorCache = new();

        public static ORSet.IDeltaGroupOperation CreateDeltaGroup(Type t,
            ImmutableArray<IReplicatedData> arr)
        {
            if (orDeltaGroupCtorCache.TryGetValue(t, out var value) == false)
            {
                value = orDeltaGroupCtorCache.GetOrAdd(t, static ct =>
                {
                    return MakeConstructorExprForTypeWith1GenericsOneString<
                        ImmutableArray<IReplicatedData>,
                        ORSet.IDeltaGroupOperation>(
                        typeof(ORSet<>.DeltaGroup), ct).Compile();
                });
            }

            return value(arr);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, IGSet, Proto.Msg.GSet>>
            gSetToProtoCache = new();

        public static Proto.Msg.GSet GSetToProto(Type k,
            ReplicatedDataSerializer serializer,
            IGSet operation)
        {
                var value = gSetToProtoCache.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericOneArg<
                                IGSet, Proto.Msg.GSet>(
                                GSetUnknownToProtoMaker, kc)
                            .Compile());

            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer,RepeatedField<OtherMessage>, IGSet>>
            toGenericGSetCache = new();

        public static IGSet ToGenericGSet(Type k,
            ReplicatedDataSerializer serializer,
            RepeatedField<OtherMessage> operation)
        {
                var value = toGenericGSetCache.GetOrAdd(k,
                    static kc =>
                       MakeInstanceCallOneGenericOneArg<RepeatedField<OtherMessage>, IGSet>(
                                GSetMaker, kc, true)
                            .Compile());

            return value(serializer,operation);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, ILWWRegister,
                    Proto.Msg.LWWRegister>>
            genericLwwRegisterToProtoCache = new();

        public static Proto.Msg.LWWRegister GenericLwwRegisterToProto(Type k,
            ReplicatedDataSerializer serializer,
            ILWWRegister operation)
        {
                var value = genericLwwRegisterToProtoCache.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericOneArg<
                                ILWWRegister, Proto.Msg.LWWRegister>(
                                LWWProtoMaker, kc)
                            .Compile());

            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, Proto.Msg.LWWRegister,
                    ILWWRegister>>
            genericLwwRegisterFromProtoCache = new();

        public static ILWWRegister GenericLwwRegisterFromProto(Type k,
            ReplicatedDataSerializer serializer,
            Proto.Msg.LWWRegister operation)
        {
            var value = genericLwwRegisterFromProtoCache.GetOrAdd(k,
                static kc =>
                    MakeInstanceCallOneGenericOneArg<
                            Proto.Msg.LWWRegister, ILWWRegister>(
                            LWWRegisterMaker, kc, true)
                        .Compile());


            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, IORDictionary, Proto.Msg.ORMap>>
            orDictionaryToProtoCache = new();

        public static Proto.Msg.ORMap ORDictionaryToProto(Type k, Type v,
            ReplicatedDataSerializer serializer,
            IORDictionary operation)
        {
            
                var value = orDictionaryToProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                IORDictionary, Proto.Msg.ORMap>(
                                ORDictProtoMaker, kc.Item1, kc.Item2)
                            .Compile());
            

            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, Proto.Msg.ORMap, IORDictionary>>
            orDictionaryFromProtoCache = new();

        public static IORDictionary ORDictionaryFromProto(Type k, Type v,
            ReplicatedDataSerializer serializer,
            Proto.Msg.ORMap operation)
        {
                var value = orDictionaryFromProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                Proto.Msg.ORMap, IORDictionary>(
                                ORDictMaker, kc.Item1, kc.Item2)
                            .Compile());
            

            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer,
                    List<ORDictionary.IDeltaOperation>,
                    Proto.Msg.ORMapDeltaGroup>>
            orDictionaryDeltasToProtoCache = new();

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, Proto.Msg.ORMapDeltaGroup,
                    ORDictionary.IDeltaGroupOp>>
            orDictionaryDeltaGroupFromProtoCache = new();

        public static Proto.Msg.ORMapDeltaGroup ORDictionaryDeltasToProto(
            Type k, Type v,
            ReplicatedDataSerializer serializer,
            List<ORDictionary.IDeltaOperation> operation)
        {
                var value = orDictionaryDeltasToProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                List<ORDictionary.IDeltaOperation>,
                                Proto.Msg.ORMapDeltaGroup>(
                                ORDeltaGroupProtoMaker, kc.Item1, kc.Item2)
                            .Compile());

            return value(serializer, operation);
        }

        public static ORDictionary.IDeltaGroupOp
            ORDictionaryDeltaGroupFromProto(Type k, Type v,
                ReplicatedDataSerializer serializer,
                Proto.Msg.ORMapDeltaGroup operation)
        {
                var value = orDictionaryDeltaGroupFromProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                Proto.Msg.ORMapDeltaGroup,
                                ORDictionary.IDeltaGroupOp>(
                                ORDeltaGroupMaker, kc.Item1, kc.Item2)
                            .Compile());

            return value(serializer, operation);
        }

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, ILWWDictionary,
                    Proto.Msg.LWWMap>>
            lwwDictionaryToProtoCache = new();

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, Proto.Msg.LWWMap,
                    ILWWDictionary>>
            lwwDictionaryFromProtoCache = new();

        private static readonly ConcurrentDictionary<TwoTypeLookup,
                Func<ReplicatedDataSerializer, ORDictionary.IDeltaOperation,
                    ILWWDictionaryDeltaOperation>>
            lwwDictionaryDeltaFromProtoCache = new();


        public static Proto.Msg.LWWMap
            LWWDictionaryToProto(Type k, Type v,
                ReplicatedDataSerializer serializer,
                ILWWDictionary operation)
        {
                var value = lwwDictionaryToProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArgOneConst<
                                ILWWDictionary, Proto.Msg.TypeDescriptor, Proto.Msg.LWWMap>(
                                LWWDictProtoMaker, GetTypeDescriptor(kc.Item2), kc.Item1, kc.Item2)
                            .Compile());

            return value(serializer, operation);
        }

        public static ILWWDictionary
            LWWDictionaryFromProto(Type k, Type v,
                ReplicatedDataSerializer serializer,
                Proto.Msg.LWWMap operation)
        {
                var value = lwwDictionaryFromProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                Proto.Msg.LWWMap,
                                ILWWDictionary>(
                                LWWDictMaker, kc.Item1, kc.Item2)
                            .Compile());

            return value(serializer, operation);
        }

        public static ILWWDictionaryDeltaOperation
            LWWDictionaryDeltaFromProto(Type k, Type v,
                ReplicatedDataSerializer serializer,
                ORDictionary.IDeltaOperation operation)
        {
                var value = lwwDictionaryDeltaFromProtoCache.GetOrAdd(new(k, v),
                    static kc =>
                        MakeInstanceCallTwoGenericsOneArg<
                                ORDictionary.IDeltaOperation,
                                ILWWDictionaryDeltaOperation>(
                                LWWDictionaryDeltaMaker, kc.Item1, kc.Item2)
                            .Compile());
            

            return value(serializer, operation);
        }


        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, IPNCounterDictionary,
                    PNCounterMap>>
            pnToProtoCache = new();

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, ORDictionary.IDeltaOperation,
                    IPNCounterDictionaryDeltaOperation>>
            pnDictionaryDeltaCache = new();

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, PNCounterMap,
                    IPNCounterDictionary>>
            pnDictionaryFromProtoCache = new();

        public static IPNCounterDictionary
            PNCounterDictionaryFromProto(Type k,
                ReplicatedDataSerializer serializer,
                PNCounterMap op)
        {
            var   value = pnDictionaryFromProtoCache.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericOneArg<
                            PNCounterMap,
                            IPNCounterDictionary>(
                            PNCounterDictMaker, kc).Compile());
            

            return value(serializer, op);
        }

        private static readonly ConcurrentDictionary<Type,
                Func<ReplicatedDataSerializer, Proto.Msg.PNCounterMap,
                    IPNCounterDictionary>>
            pnCounterDictionaryFromProtoCache = new();

        public static IPNCounterDictionaryDeltaOperation
            PnCounterDictionaryDeltaFromProto(Type k,
                ReplicatedDataSerializer serializer,
                ORDictionary.IDeltaOperation op)
        {
                var value = pnDictionaryDeltaCache.GetOrAdd(k,
                    static kc =>
                        MakeInstanceCallOneGenericOneArg<
                            ORDictionary.IDeltaOperation,
                            IPNCounterDictionaryDeltaOperation>(
                            PNCounterDeltaMaker, kc).Compile());

            return value(serializer, op);
        }

        public static PNCounterMap PNCounterDictionaryToProto(Type k,
            ReplicatedDataSerializer serializer, IPNCounterDictionary dict)
        {
                var value = pnToProtoCache.GetOrAdd(k, static kc =>
                    MakeInstanceCallOneGenericOneArg<IPNCounterDictionary,
                            PNCounterMap>(PNCounterDictProtoMaker, kc)
                        .Compile());
            
            return value(serializer, dict);
        }

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<string, IKey>>
            orMultiValueDictionaryKeyCache = new();

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<ReplicatedDataSerializer
                , IORMultiValueDictionary, Proto.Msg.ORMultiMap>>
            orMultiValueSerializerCache = new();

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<ReplicatedDataSerializer,
                Proto.Msg.ORMultiMap, IORMultiValueDictionary>>
            orMultiValueDeserializerCache = new();

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<ReplicatedDataSerializer
                , ORDictionary.IDeltaOperation, bool,
                IORMultiValueDictionaryDeltaOperation>>
            orMultiValueDeltaOpCache = new();

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<string, IKey>>
            orDictionaryKeyCache = new();

        private static readonly
            ConcurrentDictionary<TwoTypeLookup, Func<string, IKey>>
            lwwDictionaryKeyCache = new();

        private static readonly
            ConcurrentDictionary<Type, Func<string, IKey>>
            orSetKeyCache = new();

        private static readonly
            ConcurrentDictionary<Type, Func<string, IKey>>
            pnCounterDictionaryKeyCache = new();

        private static readonly
            ConcurrentDictionary<Type, Func<string, IKey>>
            lwwRegisterKeyCache = new();

        private static readonly
            ConcurrentDictionary<Type, Func<string, IKey>>
            gsetKeyCache = new();

        public static Proto.Msg.ORMultiMap
            IORMultiValueDictionaryToProtoORMultiMap(Type tk, Type tv,
                ReplicatedDataSerializer ser,
                IORMultiValueDictionary dict)
        {
            
                var value = orMultiValueSerializerCache.GetOrAdd(new(tk, tv),
                    MakeInstanceCallTwoGenericsOneArg<
                            IORMultiValueDictionary, Proto.Msg.ORMultiMap>(
                            MultiMapProtoMaker, tk,
                            tv)
                        .Compile());

            return value(ser, dict);
        }

        public static IORMultiValueDictionary
            ProtoORMultiMapToIORMultiValueDictionary(Type tk, Type tv,
                ReplicatedDataSerializer ser,
                Proto.Msg.ORMultiMap dict)
        {
                var value = orMultiValueDeserializerCache.GetOrAdd(new(tk, tv),
                    MakeInstanceCallTwoGenericsOneArg<
                            Proto.Msg.ORMultiMap, IORMultiValueDictionary>(
                            MultiDictMaker, tk,
                            tv)
                        .Compile());

            return value(ser, dict);
        }


        public static IORMultiValueDictionaryDeltaOperation
            ToOrMultiDictionaryDelta(Type tk, Type tv,
                ReplicatedDataSerializer ser,
                ORDictionary.IDeltaOperation deltaop, bool withValueDeltas)
        {
                var value = orMultiValueDeltaOpCache.GetOrAdd(new(tk, tv),
                    MakeInstanceCallTwoGenericsTwoArgs<
                            ORDictionary.IDeltaOperation, bool,
                            IORMultiValueDictionaryDeltaOperation>(
                            ORMultiDictionaryDeltaMaker, tk,
                            tv)
                        .Compile());

            return value(ser, deltaop, withValueDeltas);
        }

        public static IKey GetGSetKey(Type k, string arg)
        {
                var value = gsetKeyCache.GetOrAdd(k,
                    MakeConstructorExprForTypeWith1GenericsOneString<string,
                        IKey>(
                        typeof(GSetKey<>), k).Compile());

            return value(arg);
        }

        public static IKey GetLWWRegisterKeyValue(Type k, string arg)
        {
            var value = lwwRegisterKeyCache.GetOrAdd(k,
                MakeConstructorExprForTypeWith1GenericsOneString<string,
                    IKey>(
                    typeof(LWWRegisterKey<>), k).Compile());
            return value(arg);
        }

        public static IKey GetPNCounterDictionaryKeyValue(Type k, string arg)
        {
            
                var value = pnCounterDictionaryKeyCache.GetOrAdd(k,
                    MakeConstructorExprForTypeWith1GenericsOneString<string,
                        IKey>(
                        typeof(PNCounterDictionaryKey<>), k).Compile());

            return value(arg);
        }

        public static IKey GetORSetKey(Type k, string arg)
        {
            
            var    value = orSetKeyCache.GetOrAdd(k,
                    MakeConstructorExprForTypeWith1GenericsOneString<string,
                        IKey>(typeof(
                        ORSetKey<>), k).Compile());

            return value(arg);
        }

        public static IKey GetORDictionaryKey(Type k, Type v,
            string arg)
        {
                var value = orDictionaryKeyCache.GetOrAdd(new(k, v),
                    MakeConstructorExprForTypeWith2GenericsOneString(
                            typeof(ORDictionaryKey<,>), k, v)
                        .Compile());

            return value(arg);
        }

        public static IKey GetORMultiValueDictionaryKey(Type k, Type v,
            string arg)
        {
                var value = orMultiValueDictionaryKeyCache.GetOrAdd(new(k, v),
                    MakeConstructorExprForTypeWith2GenericsOneString(
                            typeof(ORMultiValueDictionaryKey<,>), k, v)
                        .Compile());

            return value(arg);
        }

        public static IKey GetLWWDictionaryKey(Type k, Type v, string arg)
        {
                var value = lwwDictionaryKeyCache.GetOrAdd(new(k, v),
                    MakeConstructorExprForTypeWith2GenericsOneString(
                        typeof(LWWDictionaryKey<,>), k, v).Compile());

            return value(arg);
        }

        private static
            Expression<Func<ReplicatedDataSerializer,
                TArg, TRet>>
            MakeInstanceCallOneGenericOneArg<TArg, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk,
                bool convert = false)
        {
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg),
                "multi");
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[] { tk }),
                new[] { p });
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg,
                        TRet>>(
                    convert ? Expression.Convert(c, typeof(TRet)) : c,
                    false, i, p);
        }
        
        private static
            Expression<Func<TArg, TRet>>
            MakeStaticCallOneGenericOneArg<TArg, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk,
                bool convert = false)
        {
            var p = Expression.Parameter(typeof(TArg),
                "multi");
            var c = Expression.Call(nonConstructedGenericMethodInfo.MakeGenericMethod(new[] { tk }),
                new[] { p });
            return Expression
                .Lambda<
                    Func<TArg,
                        TRet>>(
                    convert ? Expression.Convert(c, typeof(TRet)) : c,
                    false, p);
        }

        private static
            Expression<Func<
                TArg1, TArg2, TRet>>
            MakeStaticCallOneGenericTwoArgs<TArg1, TArg2, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk,
                bool convert = false)
        {
            var p = Expression.Parameter(typeof(TArg1),
                "multi");
            var p2 = Expression.Parameter(typeof(TArg2));
            var c = Expression.Call(
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[] { tk }),
                new[] { p,p2 });
            return Expression
                .Lambda<
                    Func<TArg1, TArg2,
                        TRet>>(
                    convert ? Expression.Convert(c, typeof(TRet)) : c, false, p,
                    p2);
        }

        private static Expression<Func<ReplicatedDataSerializer, TArg, TRet>>
            MakeInstanceCallOneGenericOneIgnoredArg<TArg,TRet>(
                MethodInfo nonConstructedGenericMethodInfo,
                Type tk, Type targ2Ignore, bool convert = false)
        {
            var p2 = Expression.Constant(null, targ2Ignore);
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg),
                "multi");
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[] { tk }),
                p, p2);
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg,
                        TRet>>(
                    convert ? Expression.Convert(c, typeof(TRet)) : c, false, i,
                    p);

        }
        private static
            Expression<Func<ReplicatedDataSerializer,
                TArg1, TArg2, TRet>>
            MakeInstanceCallOneGenericTwoArgs<TArg1, TArg2, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk,
                bool convert = false)
        {
            
            var p2 = Expression.Parameter(typeof(TArg2));
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg1),
                "multi");
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[] { tk }),
                new[] { p, p2 });
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg1, TArg2,
                        TRet>>(
                    convert ? Expression.Convert(c, typeof(TRet)) : c, false, i,
                    p, p2);
        }

        private static
            Expression<Func<ReplicatedDataSerializer,
                TArg, TRet>>
            MakeInstanceCallTwoGenericsOneArg<TArg, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk, Type tv)
        {
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg),
                "multi");
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[]
                {
                    tk, tv
                }),
                new[] { p });
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg,
                        TRet>>(c, false, i, p);
        }
        
        private static
            Expression<Func<ReplicatedDataSerializer,
                TArg, TRet>>
            MakeInstanceCallTwoGenericsOneArgOneConst<TArg,TConst, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, TConst conArg, Type tk, Type tv)
        {
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg),
                "multi");
            var pc = Expression.Constant(conArg, typeof(TConst));
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[]
                {
                    tk, tv
                }),
                new Expression[] { p ,pc});
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg,
                        TRet>>(c, false, i, p);
        }

        private static
            Expression<Func<ReplicatedDataSerializer,
                TArg1, TArg2, TRet>>
            MakeInstanceCallTwoGenericsTwoArgs<TArg1, TArg2, TRet>(
                MethodInfo nonConstructedGenericMethodInfo, Type tk, Type tv)
        {
            var i = Expression.Parameter(typeof(ReplicatedDataSerializer),
                "ser");
            var p = Expression.Parameter(typeof(TArg1),
                "multi");
            var p2 = Expression.Parameter(typeof(TArg2));
            var c = Expression.Call(i,
                nonConstructedGenericMethodInfo.MakeGenericMethod(new[]
                {
                    tk, tv
                }),
                new[] { p, p2 });
            return Expression
                .Lambda<
                    Func<ReplicatedDataSerializer, TArg1, TArg2,
                        TRet>>(c, false, i, p, p2);
        }

        private static Expression<Func<T, TOut>>
            MakeConstructorExprForTypeWith1GenericsOneString<T, TOut>(Type t,
                Type k)
        {
            var p = Expression.Parameter(typeof(T), "arg");
            var ctor = t
                .MakeGenericType(new[] { k })
                .GetConstructor(new[] { typeof(T) });
            var newExp = Expression.New(ctor, p);
            var cast = Expression.Convert(newExp, typeof(TOut));
            var newVal = Expression.Lambda<Func<T, TOut>>(cast, p);
            return newVal;
        }

        private static Expression<Func<string, IKey>>
            MakeConstructorExprForTypeWith2GenericsOneString(Type t, Type k,
                Type v)
        {
            var p = Expression.Parameter(typeof(string), "key");
            var ctor = t
                .MakeGenericType(new[] { k, v })
                .GetConstructor(new[] { typeof(string) });
            var newExp = Expression.New(ctor, p);
            var cast = Expression.Convert(newExp, typeof(IKey));
            var newVal = Expression.Lambda<Func<string, IKey>>(cast, p);
            return newVal;
        }
    }
}