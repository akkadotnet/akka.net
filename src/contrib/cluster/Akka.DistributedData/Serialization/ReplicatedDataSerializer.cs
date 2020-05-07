//-----------------------------------------------------------------------
// <copyright file="ReplicatedDataSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Google.Protobuf;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Akka.DistributedData.Serialization.Proto.Msg;
using Akka.Util;
using Akka.Util.Internal;
using ArgumentOutOfRangeException = System.ArgumentOutOfRangeException;
using IActorRef = Akka.Actor.IActorRef;


namespace Akka.DistributedData.Serialization
{
    public sealed class ReplicatedDataSerializer : SerializerWithStringManifest
    {

        private const string DeletedDataManifest = "A";
        private const string GSetManifest = "B";
        private const string GSetKeyManifest = "b";
        private const string ORSetManifest = "C";
        private const string ORSetKeyManifest = "c";
        private const string ORSetAddManifest = "Ca";
        private const string ORSetRemoveManifest = "Cr";
        private const string ORSetFullManifest = "Cf";
        private const string ORSetDeltaGroupManifest = "Cg";
        private const string FlagManifest = "D";
        private const string FlagKeyManifest = "d";
        private const string LWWRegisterManifest = "E";
        private const string LWWRegisterKeyManifest = "e";
        private const string GCounterManifest = "F";
        private const string GCounterKeyManifest = "f";
        private const string PNCounterManifest = "G";
        private const string PNCounterKeyManifest = "g";
        private const string ORMapManifest = "H";
        private const string ORMapKeyManifest = "h";
        private const string ORMapPutManifest = "Ha";
        private const string ORMapRemoveManifest = "Hr";
        private const string ORMapRemoveKeyManifest = "Hk";
        private const string ORMapUpdateManifest = "Hu";
        private const string ORMapDeltaGroupManifest = "Hg";
        private const string LWWMapManifest = "I";
        private const string LWWMapDeltaGroupManifest = "Ig";
        private const string LWWMapKeyManifest = "i";
        private const string PNCounterMapManifest = "J";
        private const string PNCounterMapDeltaOperationManifest = "Jo";
        private const string PNCounterMapKeyManifest = "j";
        private const string ORMultiMapManifest = "K";
        private const string ORMultiMapDeltaOperationManifest = "Ko";
        private const string ORMultiMapKeyManifest = "k";
        private const string VersionVectorManifest = "L";

        private readonly SerializationSupport _ser;

        private readonly byte[] _emptyArray = Array.Empty<byte>();

        public ReplicatedDataSerializer(ExtendedActorSystem system) : base(system)
        {
            _ser = new SerializationSupport(system);
        }


        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case IORSet o: return SerializationSupport.Compress(ToProto(o));
                case ORSet.IAddDeltaOperation o: return ToProto(o.UnderlyingSerialization).ToByteArray();
                case ORSet.IRemoveDeltaOperation o: return ToProto(o.UnderlyingSerialization).ToByteArray();
                case IGSet g: return ToProto(g).ToByteArray();
                case GCounter g: return ToProto(g).ToByteArray();
                case PNCounter p: return ToProto(p).ToByteArray();
                case Flag f: return ToProto(f).ToByteArray();
                case ILWWRegister l: return ToProto(l).ToByteArray();
                case IORDictionary o: return SerializationSupport.Compress(ToProto(o));
                case ORDictionary.IDeltaOperation p: return ToProto(p).ToByteArray();
                case ILWWDictionary l: return SerializationSupport.Compress(ToProto(l));
                case ILWWDictionaryDeltaOperation ld: return ToProto(ld.Underlying).ToByteArray();
                case IPNCounterDictionary pn: return SerializationSupport.Compress(ToProto(pn));
                case IPNCounterDictionaryDeltaOperation pnd: return ToProto(pnd.Underlying).ToByteArray();
                case IORMultiValueDictionary m: return SerializationSupport.Compress(ToProto(m));
                case IORMultiValueDictionaryDeltaOperation md: return ToProto(md).ToByteArray();
                case DeletedData _: return _emptyArray;
                case VersionVector v: return SerializationSupport.VersionVectorToProto(v).ToByteArray();
                // key types
                case IKey k: return ToProto(k).ToByteArray();
                // less common delta types
                case ORSet.IDeltaGroupOperation o: return ToProto(o).ToByteArray();
                case ORSet.IFullStateDeltaOperation o: return ToProto(o.UnderlyingSerialization).ToByteArray();
                default:
                    throw new ArgumentException($"Can't serialize object of type [{obj.GetType().FullName}] in [{GetType().FullName}]");
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case ORSetManifest: return ORSetFromBinary(SerializationSupport.Decompress(bytes));
                case ORSetAddManifest: return ORAddDeltaOperationFromBinary(bytes);
                case ORSetRemoveManifest: return ORRemoveOperationFromBinary(bytes);
                case GSetManifest: return GSetFromBinary(bytes);
                case GCounterManifest: return GCounterFromBytes(bytes);
                case PNCounterManifest: return PNCounterFromBytes(bytes);
                case FlagManifest: return FlagFromBinary(bytes);
                case LWWRegisterManifest: return LWWRegisterFromBinary(bytes);
                case ORMapManifest: return ORDictionaryFromBinary(SerializationSupport.Decompress(bytes));
                case ORMapPutManifest: return ORDictionaryPutFromBinary(bytes);
                case ORMapRemoveManifest: return ORDictionaryRemoveFromBinary(bytes);
                case ORMapRemoveKeyManifest: return ORDictionaryRemoveKeyFromBinary(bytes);
                case ORMapUpdateManifest: return ORDictionaryUpdateFromBinary(bytes);
                case ORMapDeltaGroupManifest: return ORDictionaryDeltaGroupFromBinary(bytes);
                case LWWMapManifest: return LWWDictionaryFromBinary(SerializationSupport.Decompress(bytes));
                case LWWMapDeltaGroupManifest:
                    return LWWDictionaryDeltaGroupFromBinary(bytes);
                case PNCounterMapManifest: return PNCounterDictionaryFromBinary(SerializationSupport.Decompress(bytes));
                case PNCounterMapDeltaOperationManifest: return PNCounterDeltaFromBinary(bytes);
                case ORMultiMapManifest: return ORMultiDictionaryFromBinary(SerializationSupport.Decompress(bytes));
                case ORMultiMapDeltaOperationManifest: return ORMultiDictionaryDeltaFromBinary(bytes);
                case DeletedDataManifest: return DeletedData.Instance;
                case VersionVectorManifest: return _ser.VersionVectorFromBinary(bytes);

                // key types
                case ORSetKeyManifest: return ORSetKeyFromBinary(bytes);
                case GSetKeyManifest: return GSetKeyFromBinary(bytes);
                case GCounterKeyManifest: return GCounterKeyFromBinary(bytes);
                case PNCounterKeyManifest: return PNCounterKeyFromBinary(bytes);
                case FlagKeyManifest: return FlagKeyFromBinary(bytes);
                case LWWRegisterKeyManifest: return LWWRegisterKeyFromBinary(bytes);
                case ORMapKeyManifest: return ORDictionaryKeyFromBinary(bytes);
                case LWWMapKeyManifest: return LWWDictionaryKeyFromBinary(bytes);
                case PNCounterMapKeyManifest: return PNCounterDictionaryKeyFromBinary(bytes);
                case ORMultiMapKeyManifest: return ORMultiValueDictionaryKeyFromBinary(bytes);

                // less common delta types
                case ORSetDeltaGroupManifest: return ORDeltaGroupOperationFromBinary(bytes);
                case ORSetFullManifest: return ORFullStateDeltaOperationFromBinary(bytes);
                default:
                    throw new ArgumentException($"Can't deserialize object with unknown manifest [{manifest}]");
            }
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case IORSet _: return ORSetManifest;
                case ORSet.IAddDeltaOperation _: return ORSetAddManifest;
                case ORSet.IRemoveDeltaOperation _: return ORSetRemoveManifest;
                case IGSet _: return GSetManifest;
                case GCounter _: return GCounterManifest;
                case PNCounter _: return PNCounterManifest;
                case Flag _: return FlagManifest;
                case ILWWRegister _: return LWWRegisterManifest;
                case IORDictionary _: return ORMapManifest;
                case ORDictionary.IPutDeltaOp _: return ORMapPutManifest;
                case ORDictionary.IRemoveDeltaOp _: return ORMapRemoveManifest;
                case ORDictionary.IRemoveKeyDeltaOp _: return ORMapRemoveKeyManifest;
                case ORDictionary.IUpdateDeltaOp _: return ORMapUpdateManifest;
                case ILWWDictionary _: return LWWMapManifest;
                case ILWWDictionaryDeltaOperation _: return LWWMapDeltaGroupManifest;
                case IPNCounterDictionary _: return PNCounterMapManifest;
                case IPNCounterDictionaryDeltaOperation _: return PNCounterMapDeltaOperationManifest;
                case IORMultiValueDictionary _: return ORMultiMapManifest;
                case IORMultiValueDictionaryDeltaOperation _: return ORMultiMapDeltaOperationManifest;
                case DeletedData _: return DeletedDataManifest;
                case VersionVector _: return VersionVectorManifest;

                // key types
                case IORSetKey _: return ORSetKeyManifest;
                case IGSetKey _: return GSetKeyManifest;
                case GCounterKey _: return GCounterKeyManifest;
                case PNCounterKey _: return PNCounterKeyManifest;
                case FlagKey _: return FlagKeyManifest;
                case ILWWRegisterKey _: return LWWRegisterKeyManifest;
                case IORDictionaryKey _: return ORMapKeyManifest;
                case ILWWDictionaryKey _: return LWWMapKeyManifest;
                case IPNCounterDictionaryKey _: return PNCounterMapKeyManifest;
                case IORMultiValueDictionaryKey _: return ORMultiMapKeyManifest;

                // less common delta types
                case ORSet.IDeltaGroupOperation _: return ORSetDeltaGroupManifest;
                case ORDictionary.IDeltaGroupOp _: return ORMapDeltaGroupManifest;
                case ORSet.IFullStateDeltaOperation _: return ORSetFullManifest;

                default:
                    throw new ArgumentException($"Can't serialize object of type [{o.GetType().FullName}] in [{GetType().FullName}]");
            }
        }

        private static TypeDescriptor GetTypeDescriptor(Type t)
        {
            var typeInfo = new TypeDescriptor();
            if (t == typeof(string))
            {
                typeInfo.Type = ValType.String;
            }
            else if (t == typeof(int))
            {
                typeInfo.Type = ValType.Int;
            }
            else if (t == typeof(long))
            {
                typeInfo.Type = ValType.Long;
            }
            else if (t == typeof(IActorRef))
            {
                typeInfo.Type = ValType.ActorRef;
            }
            else
            {
                typeInfo.Type = ValType.Other;
                typeInfo.TypeName = t.TypeQualifiedName();
            }

            return typeInfo;
        }

        private static Type GetTypeFromDescriptor(TypeDescriptor t)
        {
            switch (t.Type)
            {
                case ValType.Int:
                    return typeof(int);
                case ValType.Long:
                    return typeof(long);
                case ValType.String:
                    return typeof(string);
                case ValType.ActorRef:
                    return typeof(IActorRef);
                case ValType.Other:
                    {
                        var type = Type.GetType(t.TypeName);
                        return type;
                    }
                default:
                    throw new SerializationException($"Unknown ValType of [{t.Type}] detected");
            }
        }

        #region ORSet

        private static Proto.Msg.ORSet ORSetToProto<T>(ORSet<T> set)
        {
            var p = new Proto.Msg.ORSet();
            p.Vvector = SerializationSupport.VersionVectorToProto(set.VersionVector);
            p.Dots.Add(set.ElementsMap.Values.Select(SerializationSupport.VersionVectorToProto));
            p.TypeInfo = new TypeDescriptor();
            return p;
        }
        private IORSet ORSetFromBinary(byte[] bytes)
        {
            return FromProto(Proto.Msg.ORSet.Parser.ParseFrom(bytes));
        }

        private Proto.Msg.ORSet ToProto(IORSet orset)
        {
            switch (orset)
            {
                case ORSet<int> ints:
                    {
                        var p = ORSetToProto(ints);
                        p.TypeInfo.Type = ValType.Int;
                        p.IntElements.Add(ints.Elements);
                        return p;
                    }
                case ORSet<long> longs:
                    {
                        var p = ORSetToProto(longs);
                        p.TypeInfo.Type = ValType.Long;
                        p.LongElements.Add(longs.Elements);
                        return p;
                    }
                case ORSet<string> strings:
                    {
                        var p = ORSetToProto(strings);
                        p.TypeInfo.Type = ValType.String;
                        p.StringElements.Add(strings.Elements);
                        return p;
                    }
                case ORSet<IActorRef> refs:
                    {
                        var p = ORSetToProto(refs);
                        p.TypeInfo.Type = ValType.ActorRef;
                        p.ActorRefElements.Add(refs.Select(Akka.Serialization.Serialization.SerializedActorPath));
                        return p;
                    }
                default: // unknown type
                    {
                        // runtime type - enter horrible dynamic serialization stuff
                        var makeProto = ORSetUnknownMaker.MakeGenericMethod(orset.SetType);
                        return (Proto.Msg.ORSet)makeProto.Invoke(this, new object[] { orset });
                    }
            }
        }

        private IORSet FromProto(Proto.Msg.ORSet orset)
        {
            var dots = orset.Dots.Select(x => _ser.VersionVectorFromProto(x));
            var vector = _ser.VersionVectorFromProto(orset.Vvector);

            if (orset.IntElements.Count > 0 || orset.TypeInfo.Type == ValType.Int)
            {
                var eInt = orset.IntElements.Zip(dots, (i, versionVector) => (i, versionVector))
                    .ToImmutableDictionary(x => x.i, y => y.versionVector);

                return new ORSet<int>(eInt, vector);
            }

            if (orset.LongElements.Count > 0 || orset.TypeInfo.Type == ValType.Long)
            {
                var eLong = orset.LongElements.Zip(dots, (i, versionVector) => (i, versionVector))
                    .ToImmutableDictionary(x => x.i, y => y.versionVector);
                return new ORSet<long>(eLong, vector);
            }

            if (orset.StringElements.Count > 0 || orset.TypeInfo.Type == ValType.String)
            {
                var eStr = orset.StringElements.Zip(dots, (i, versionVector) => (i, versionVector))
                    .ToImmutableDictionary(x => x.i, y => y.versionVector);
                return new ORSet<string>(eStr, vector);
            }

            if (orset.ActorRefElements.Count > 0 || orset.TypeInfo.Type == ValType.ActorRef)
            {
                var eRef = orset.ActorRefElements.Zip(dots, (i, versionVector) => (i, versionVector))
                    .ToImmutableDictionary(x => _ser.ResolveActorRef(x.i), y => y.versionVector);
                return new ORSet<IActorRef>(eRef, vector);
            }

            // runtime type - enter horrible dynamic serialization stuff

            var setContentType = Type.GetType(orset.TypeInfo.TypeName);

            var eOther = orset.OtherElements.Zip(dots,
                (i, versionVector) => (_ser.OtherMessageFromProto(i), versionVector))
                .ToImmutableDictionary(x => x.Item1, x => x.versionVector);

            var setType = ORSetMaker.MakeGenericMethod(setContentType);
            return (IORSet)setType.Invoke(this, new object[] { eOther, vector });
        }

        private static readonly MethodInfo ORSetMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ToGenericORSet), BindingFlags.Static | BindingFlags.NonPublic);

        private static ORSet<T> ToGenericORSet<T>(ImmutableDictionary<object, VersionVector> elems, VersionVector vector)
        {
            var finalInput = elems.ToImmutableDictionary(x => (T)x.Key, v => v.Value);

            return new ORSet<T>(finalInput, vector);
        }

        private static readonly MethodInfo ORSetUnknownMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ORSetUnknownToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>
        /// Called when we're serializing none of the standard object types with ORSet
        /// </summary>
        private Proto.Msg.ORSet ORSetUnknownToProto<T>(IORSet o)
        {
            var orset = (ORSet<T>)o;
            var p = ORSetToProto(orset);
            p.TypeInfo.Type = ValType.Other;
            p.TypeInfo.TypeName = typeof(T).TypeQualifiedName();
            p.OtherElements.Add(orset.Elements.Select(x => _ser.OtherMessageToProto(x)));
            return p;
        }

        private ORSet.IAddDeltaOperation ORAddDeltaOperationFromBinary(byte[] bytes)
        {
            var set = FromProto(Proto.Msg.ORSet.Parser.ParseFrom(bytes));
            return set.ToAddDeltaOperation();
        }

        private ORSet.IRemoveDeltaOperation ORRemoveOperationFromBinary(byte[] bytes)
        {
            var set = FromProto(Proto.Msg.ORSet.Parser.ParseFrom(bytes));
            return set.ToRemoveDeltaOperation();
        }

        private ORSet.IFullStateDeltaOperation ORFullStateDeltaOperationFromBinary(byte[] bytes)
        {
            var set = FromProto(Proto.Msg.ORSet.Parser.ParseFrom(bytes));
            return set.ToFullStateDeltaOperation();
        }

        private Proto.Msg.ORSetDeltaGroup ToProto(ORSet.IDeltaGroupOperation orset)
        {
            var deltaGroup = new Proto.Msg.ORSetDeltaGroup();

            var gatheredTypeInfo = false;

            void SetType(IORSet underlying)
            {
                if (!gatheredTypeInfo) // only need to do this once - all Deltas must have ORSet<T> of same <T>
                {
                    deltaGroup.TypeInfo = GetTypeDescriptor(underlying.SetType);
                }
                gatheredTypeInfo = true;
            }

            foreach (var op in orset.OperationsSerialization)
            {
                switch (op)
                {
                    case ORSet.IAddDeltaOperation add:
                        deltaGroup.Entries.Add(new ORSetDeltaGroup.Types.Entry() { Operation = ORSetDeltaOp.Add, Underlying = ToProto(add.UnderlyingSerialization) });
                        SetType(add.UnderlyingSerialization);
                        break;
                    case ORSet.IRemoveDeltaOperation remove:
                        deltaGroup.Entries.Add(new ORSetDeltaGroup.Types.Entry() { Operation = ORSetDeltaOp.Remove, Underlying = ToProto(remove.UnderlyingSerialization) });
                        SetType(remove.UnderlyingSerialization);
                        break;
                    case ORSet.IFullStateDeltaOperation full:
                        deltaGroup.Entries.Add(new ORSetDeltaGroup.Types.Entry() { Operation = ORSetDeltaOp.Full, Underlying = ToProto(full.UnderlyingSerialization) });
                        SetType(full.UnderlyingSerialization);
                        break;
                    default: throw new ArgumentException($"{op} should not be nested");
                }
            }

            return deltaGroup;
        }

        private ORSet.IDeltaGroupOperation ORDeltaGroupOperationFromBinary(byte[] bytes)
        {
            var deltaGroup = Proto.Msg.ORSetDeltaGroup.Parser.ParseFrom(bytes);
            var ops = new List<ORSet.IDeltaOperation>();

            foreach (var op in deltaGroup.Entries)
            {
                switch (op.Operation)
                {
                    case ORSetDeltaOp.Add:
                        ops.Add(FromProto(op.Underlying).ToAddDeltaOperation());
                        break;
                    case ORSetDeltaOp.Remove:
                        ops.Add(FromProto(op.Underlying).ToRemoveDeltaOperation());
                        break;
                    case ORSetDeltaOp.Full:
                        ops.Add(FromProto(op.Underlying).ToFullStateDeltaOperation());
                        break;
                    default:
                        throw new SerializationException($"Unknown ORSet delta operation ${op.Operation}");

                }
            }

            var arr = ops.Cast<IReplicatedData>().ToImmutableArray();

            switch (deltaGroup.TypeInfo.Type)
            {
                case ValType.Int:
                    return new ORSet<int>.DeltaGroup(arr);
                case ValType.Long:
                    return new ORSet<long>.DeltaGroup(arr);
                case ValType.String:
                    return new ORSet<string>.DeltaGroup(arr);
                case ValType.ActorRef:
                    return new ORSet<IActorRef>.DeltaGroup(arr);
            }

            // if we made it this far, we're working with an object type
            // enter reflection magic

            var type = Type.GetType(deltaGroup.TypeInfo.TypeName);
            var orDeltaGroupType = typeof(ORSet<>.DeltaGroup).MakeGenericType(type);
            return (ORSet.IDeltaGroupOperation)Activator.CreateInstance(orDeltaGroupType, arr);
        }

        #endregion

        #region GSet

        private Proto.Msg.GSet GSetToProto<T>(GSet<T> gset)
        {
            var p = new Proto.Msg.GSet();
            p.TypeInfo = GetTypeDescriptor(typeof(T));
            return p;
        }

        private Proto.Msg.GSet GSetToProtoUnknown<T>(IGSet g)
        {
            var gset = (GSet<T>)g;
            var p = new Proto.Msg.GSet();
            p.TypeInfo = GetTypeDescriptor(typeof(T));
            p.OtherElements.Add(gset.Select(x => _ser.OtherMessageToProto(x)));
            return p;
        }

        private static readonly MethodInfo GSetUnknownToProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GSetToProtoUnknown), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.GSet ToProto(IGSet gset)
        {
            switch (gset)
            {
                case GSet<int> ints:
                    {
                        var p = GSetToProto(ints);
                        p.IntElements.Add(ints.Elements);
                        return p;
                    }
                case GSet<long> longs:
                    {
                        var p = GSetToProto(longs);
                        p.LongElements.Add(longs.Elements);
                        return p;
                    }
                case GSet<string> strings:
                    {
                        var p = GSetToProto(strings);
                        p.StringElements.Add(strings.Elements);
                        return p;
                    }
                case GSet<IActorRef> refs:
                    {
                        var p = GSetToProto(refs);
                        p.ActorRefElements.Add(refs.Select(Akka.Serialization.Serialization.SerializedActorPath));
                        return p;
                    }
                default: // unknown type
                    {
                        var protoMaker = GSetUnknownToProtoMaker.MakeGenericMethod(gset.SetType);
                        return (Proto.Msg.GSet)protoMaker.Invoke(this, new object[] { gset });
                    }
            }
        }

        private IGSet GSetFromBinary(byte[] bytes)
        {
            var gset = Proto.Msg.GSet.Parser.ParseFrom(bytes);

            switch (gset.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        var eInt = gset.IntElements.ToImmutableHashSet();

                        return new GSet<int>(eInt);
                    }
                case ValType.Long:
                    {
                        var eLong = gset.LongElements.ToImmutableHashSet();

                        return new GSet<long>(eLong);
                    }
                case ValType.String:
                    {
                        var eStr = gset.StringElements.ToImmutableHashSet();
                        return new GSet<string>(eStr);
                    }
                case ValType.ActorRef:
                    {
                        var eRef = gset.ActorRefElements.Select(x => _ser.ResolveActorRef(x)).ToImmutableHashSet();
                        return new GSet<IActorRef>(eRef);
                    }
                case ValType.Other:
                    {
                        // runtime type - enter horrible dynamic serialization stuff

                        var setContentType = Type.GetType(gset.TypeInfo.TypeName);

                        var eOther = gset.OtherElements.Select(x => _ser.OtherMessageFromProto(x));

                        var setType = GSetMaker.MakeGenericMethod(setContentType);
                        return (IGSet)setType.Invoke(this, new object[] { eOther });
                    }
                default:
                    throw new SerializationException($"Unknown ValType of [{gset.TypeInfo.Type}] detected while deserializing GSet");
            }
        }

        private static readonly MethodInfo GSetMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ToGenericGSet), BindingFlags.Static | BindingFlags.NonPublic);

        private static GSet<T> ToGenericGSet<T>(IEnumerable<object> items)
        {
            return new GSet<T>(items.Cast<T>().ToImmutableHashSet());
        }

        #endregion

        #region GCounter

        private Proto.Msg.GCounter ToProto(GCounter counter)
        {
            var gProto = new Proto.Msg.GCounter();

            gProto.Entries.AddRange(counter.State.Select(x => new Proto.Msg.GCounter.Types.Entry() { Node = SerializationSupport.UniqueAddressToProto(x.Key), Value = ByteString.CopyFrom(BitConverter.GetBytes(x.Value)) }));

            return gProto;
        }

        private GCounter GCounterFromBytes(byte[] bytes)
        {
            var gProto = Proto.Msg.GCounter.Parser.ParseFrom(bytes);

            return GCounterFromProto(gProto);
        }

        private GCounter GCounterFromProto(Proto.Msg.GCounter gProto)
        {
            var entries = gProto.Entries.ToImmutableDictionary(k => _ser.UniqueAddressFromProto(k.Node),
                v => BitConverter.ToUInt64(v.Value.ToByteArray(), 0));

            return new GCounter(entries);
        }

        #endregion

        #region PNCounter

        private Proto.Msg.PNCounter ToProto(PNCounter counter)
        {
            var pProto = new Proto.Msg.PNCounter();
            pProto.Increments = ToProto(counter.Increments);
            pProto.Decrements = ToProto(counter.Decrements);
            return pProto;
        }

        private PNCounter PNCounterFromBytes(byte[] bytes)
        {
            var pProto = Proto.Msg.PNCounter.Parser.ParseFrom(bytes);
            return PNCounterFromProto(pProto);
        }

        private PNCounter PNCounterFromProto(Proto.Msg.PNCounter pProto)
        {
            var increments = GCounterFromProto(pProto.Increments);
            var decrements = GCounterFromProto(pProto.Decrements);

            return new PNCounter(increments, decrements);
        }

        #endregion

        #region Flag

        private Proto.Msg.Flag ToProto(Flag flag)
        {
            var pFlag = new Proto.Msg.Flag();
            pFlag.Enabled = flag;
            return pFlag;
        }

        private Flag FlagFromProto(Proto.Msg.Flag flag)
        {
            return flag.Enabled ? Flag.True : Flag.False;
        }

        private Flag FlagFromBinary(byte[] bytes)
        {
            return FlagFromProto(Proto.Msg.Flag.Parser.ParseFrom(bytes));
        }

        #endregion

        #region LWWRegister

        private Proto.Msg.LWWRegister ToProto(ILWWRegister register)
        {
            var protoMaker = LWWProtoMaker.MakeGenericMethod(register.RegisterType);
            return (Proto.Msg.LWWRegister)protoMaker.Invoke(this, new object[] { register });
        }

        private static readonly MethodInfo LWWProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(LWWToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.LWWRegister LWWToProto<T>(ILWWRegister r)
        {
            var register = (LWWRegister<T>)r;
            var pLww = new Proto.Msg.LWWRegister();
            pLww.Node = SerializationSupport.UniqueAddressToProto(register.UpdatedBy);
            pLww.State = _ser.OtherMessageToProto(register.Value);
            pLww.Timestamp = register.Timestamp;
            pLww.TypeInfo = GetTypeDescriptor(r.RegisterType);
            return pLww;
        }

        private ILWWRegister LWWRegisterFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.LWWRegister.Parser.ParseFrom(bytes);
            return LWWRegisterFromProto(proto);
        }

        private ILWWRegister LWWRegisterFromProto(Proto.Msg.LWWRegister proto)
        {
            switch (proto.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        return GenericLWWRegisterFromProto<int>(proto);
                    }
                case ValType.Long:
                    {
                        return GenericLWWRegisterFromProto<long>(proto);
                    }
                case ValType.String:
                    {
                        return GenericLWWRegisterFromProto<string>(proto);
                    }
                case ValType.ActorRef:
                    {
                        return GenericLWWRegisterFromProto<IActorRef>(proto);
                    }
                case ValType.Other:
                    {
                        // runtime type - enter horrible dynamic serialization stuff

                        var setContentType = Type.GetType(proto.TypeInfo.TypeName);

                        var setType = LWWRegisterMaker.MakeGenericMethod(setContentType);
                        return (ILWWRegister)setType.Invoke(this, new object[] { proto });
                    }
                default:
                    throw new SerializationException($"Unknown ValType of [{proto.TypeInfo.Type}] detected while deserializing LWWRegister");
            }
        }

        private static readonly MethodInfo LWWRegisterMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericLWWRegisterFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private LWWRegister<T> GenericLWWRegisterFromProto<T>(Proto.Msg.LWWRegister proto)
        {
            var msg = (T)_ser.OtherMessageFromProto(proto.State);
            var updatedBy = _ser.UniqueAddressFromProto(proto.Node);

            return new LWWRegister<T>(updatedBy, msg, proto.Timestamp);
        }

        #endregion

        #region ORMap

        private Proto.Msg.ORMap ToProto(IORDictionary ormap)
        {
            var protoMaker = ORDictProtoMaker.MakeGenericMethod(ormap.KeyType, ormap.ValueType);
            return (Proto.Msg.ORMap)protoMaker.Invoke(this, new object[] { ormap });
        }

        private static readonly MethodInfo ORDictProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ORDictToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.ORMap ORDictToProto<TKey, TValue>(IORDictionary o) where TValue : IReplicatedData<TValue>
        {
            var ormap = (ORDictionary<TKey, TValue>)o;
            var proto = new Proto.Msg.ORMap();
            ToORMapEntries(ormap.Entries, proto);
            proto.Keys = ToProto(ormap.KeySet);
            proto.ValueTypeInfo = GetTypeDescriptor(typeof(TValue));
            return proto;
        }

        private void ToORMapEntries<TKey, TValue>(IImmutableDictionary<TKey, TValue> ormapEntries, ORMap proto) where TValue : IReplicatedData<TValue>
        {
            var entries = new List<ORMap.Types.Entry>();
            foreach (var e in ormapEntries)
            {
                var entry = new ORMap.Types.Entry();
                switch (e.Key)
                {
                    case int i:
                        entry.IntKey = i;
                        break;
                    case long l:
                        entry.LongKey = l;
                        break;
                    case string str:
                        entry.StringKey = str;
                        break;
                    default:
                        entry.OtherKey = _ser.OtherMessageToProto(e.Key);
                        break;
                }

                entry.Value = _ser.OtherMessageToProto(e.Value);
                entries.Add(entry);
            }
            proto.Entries.Add(entries);
        }

        private static readonly MethodInfo ORDictMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericORDictionaryFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private IORDictionary ORDictionaryFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.ORMap.Parser.ParseFrom(bytes);
            return ORDictionaryFromProto(proto);
        }

        private IORDictionary ORDictionaryFromProto(Proto.Msg.ORMap proto)
        {
            var keyType = GetTypeFromDescriptor(proto.Keys.TypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);
            var protoMaker = ORDictMaker.MakeGenericMethod(keyType, valueType);
            return (IORDictionary)protoMaker.Invoke(this, new object[] { proto });
        }

        private IORDictionary GenericORDictionaryFromProto<TKey, TValue>(Proto.Msg.ORMap proto) where TValue : IReplicatedData<TValue>
        {
            var keys = FromProto(proto.Keys);
            switch (proto.Keys.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.IntKey,
                            v => (TValue)_ser.OtherMessageFromProto(v.Value));
                        return new ORDictionary<int, TValue>((ORSet<int>)keys, entries);
                    }
                case ValType.Long:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.LongKey,
                            v => (TValue)_ser.OtherMessageFromProto(v.Value));
                        return new ORDictionary<long, TValue>((ORSet<long>)keys, entries);
                    }
                case ValType.String:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.StringKey,
                            v => (TValue)_ser.OtherMessageFromProto(v.Value));
                        return new ORDictionary<string, TValue>((ORSet<string>)keys, entries);
                    }
                default:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => (TKey)_ser.OtherMessageFromProto(x.OtherKey),
                            v => (TValue)_ser.OtherMessageFromProto(v.Value));

                        return new ORDictionary<TKey, TValue>((ORSet<TKey>)keys, entries);
                    }
            }
        }

        private Proto.Msg.ORMapDeltaGroup ORDictionaryDeltasToProto(
            List<ORDictionary.IDeltaOperation> deltaGroupOps)
        {
            var keyType = deltaGroupOps[0].KeyType;
            var valueType = deltaGroupOps[0].ValueType;

            var protoMaker = ORDeltaGroupProtoMaker.MakeGenericMethod(keyType, valueType);
            return (Proto.Msg.ORMapDeltaGroup)protoMaker.Invoke(this, new object[] { deltaGroupOps });
        }

        private static readonly MethodInfo ORDeltaGroupProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ORDictionaryDeltaGroupToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.ORMapDeltaGroup ORDictionaryDeltaGroupToProto<TKey, TValue>(
            List<ORDictionary.IDeltaOperation> deltaGroupOps) where TValue : IReplicatedData<TValue>
        {
            var group = new ORMapDeltaGroup();
            group.KeyTypeInfo = GetTypeDescriptor(typeof(TKey));
            group.ValueTypeInfo = GetTypeDescriptor(typeof(TValue));

            ORMapDeltaGroup.Types.MapEntry CreateMapEntry(TKey key, object value = null)
            {
                var entry = new ORMapDeltaGroup.Types.MapEntry();
                switch (key)
                {
                    case int i:
                        entry.IntKey = i;
                        break;
                    case long l:
                        entry.LongKey = l;
                        break;
                    case string s:
                        entry.StringKey = s;
                        break;
                    default:
                        entry.OtherKey = _ser.OtherMessageToProto(key);
                        break;
                }

                if (value != null)
                    entry.Value = _ser.OtherMessageToProto(value);
                return entry;
            }

            ORMapDeltaGroup.Types.Entry CreateEntry(ORDictionary<TKey, TValue>.IDeltaOperation op)
            {
                var entry = new ORMapDeltaGroup.Types.Entry();
                switch (op)
                {
                    case ORDictionary<TKey, TValue>.PutDeltaOperation putDelta:
                        entry.Operation = ORMapDeltaOp.OrmapPut;
                        entry.Underlying = ToProto(putDelta.Underlying.AsInstanceOf<ORSet.IDeltaOperation>()
                            .UnderlyingSerialization);
                        entry.EntryData.Add(CreateMapEntry(putDelta.Key, putDelta.Value));
                        break;
                    case ORDictionary<TKey, TValue>.UpdateDeltaOperation upDelta:
                        entry.Operation = ORMapDeltaOp.OrmapUpdate;
                        entry.Underlying = ToProto(upDelta.Underlying.AsInstanceOf<ORSet.IDeltaOperation>()
                            .UnderlyingSerialization);
                        entry.EntryData.AddRange(upDelta.Values.Select(x => CreateMapEntry(x.Key, x.Value)).ToList());
                        break;
                    case ORDictionary<TKey, TValue>.RemoveDeltaOperation removeDelta:
                        entry.Operation = ORMapDeltaOp.OrmapRemove;
                        entry.Underlying = ToProto(removeDelta.Underlying.AsInstanceOf<ORSet.IDeltaOperation>()
                            .UnderlyingSerialization);
                        break;
                    case ORDictionary<TKey, TValue>.RemoveKeyDeltaOperation removeKeyDelta:
                        entry.Operation = ORMapDeltaOp.OrmapRemoveKey;
                        entry.Underlying = ToProto(removeKeyDelta.Underlying.AsInstanceOf<ORSet.IDeltaOperation>()
                            .UnderlyingSerialization);
                        entry.EntryData.Add(CreateMapEntry(removeKeyDelta.Key));
                        break;
                    default:
                        throw new SerializationException($"Unknown ORDictionary delta type {op.GetType()}");
                }

                return entry;
            }

            group.Entries.Add(deltaGroupOps.Cast<ORDictionary<TKey, TValue>.IDeltaOperation>().Select(x => CreateEntry(x)).ToList());
            return group;
        }

        private Proto.Msg.ORMapDeltaGroup ToProto(ORDictionary.IDeltaOperation op)
        {
            switch (op)
            {
                case ORDictionary.IPutDeltaOp p: return ORDictionaryPutToProto(p);
                case ORDictionary.IRemoveDeltaOp r: return ORDictionaryRemoveToProto(r);
                case ORDictionary.IRemoveKeyDeltaOp r: return ORDictionaryRemoveKeyToProto(r);
                case ORDictionary.IUpdateDeltaOp u: return ORDictionaryUpdateToProto(u);
                case ORDictionary.IDeltaGroupOp g: return ORDictionaryDeltasToProto(g.OperationsSerialization.ToList());
                default:
                    throw new SerializationException($"Unrecognized delta operation [{op}]");
            }

        }

        private Proto.Msg.ORMapDeltaGroup ORDictionaryPutToProto(ORDictionary.IPutDeltaOp op)
        {
            return ORDictionaryDeltasToProto(new List<ORDictionary.IDeltaOperation>() { op });
        }

        private Proto.Msg.ORMapDeltaGroup ORDictionaryRemoveToProto(ORDictionary.IRemoveDeltaOp op)
        {
            return ORDictionaryDeltasToProto(new List<ORDictionary.IDeltaOperation>() { op });
        }

        private Proto.Msg.ORMapDeltaGroup ORDictionaryRemoveKeyToProto(ORDictionary.IRemoveKeyDeltaOp op)
        {
            return ORDictionaryDeltasToProto(new List<ORDictionary.IDeltaOperation>() { op });
        }

        private Proto.Msg.ORMapDeltaGroup ORDictionaryUpdateToProto(ORDictionary.IUpdateDeltaOp op)
        {
            return ORDictionaryDeltasToProto(new List<ORDictionary.IDeltaOperation>() { op });
        }

        private ORDictionary.IDeltaGroupOp ORDictionaryDeltaGroupFromProto(Proto.Msg.ORMapDeltaGroup deltaGroup)
        {
            var keyType = GetTypeFromDescriptor(deltaGroup.KeyTypeInfo);
            var valueType = GetTypeFromDescriptor(deltaGroup.ValueTypeInfo);

            var groupMaker = ORDeltaGroupMaker.MakeGenericMethod(keyType, valueType);
            return (ORDictionary.IDeltaGroupOp)groupMaker.Invoke(this, new object[] { deltaGroup });
        }

        private static readonly MethodInfo ORDeltaGroupMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericORDictionaryDeltaGroupFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private ORDictionary.IDeltaGroupOp GenericORDictionaryDeltaGroupFromProto<TKey, TValue>(Proto.Msg.ORMapDeltaGroup deltaGroup) where TValue : IReplicatedData<TValue>
        {
            var deltaOps = new List<ORDictionary<TKey, TValue>.IDeltaOperation>();

            (object key, object value) MapEntryFromProto(ORMapDeltaGroup.Types.MapEntry entry)
            {
                object k = null;
                switch (deltaGroup.KeyTypeInfo.Type)
                {
                    case ValType.Int:
                        k = entry.IntKey;
                        break;
                    case ValType.Long:
                        k = entry.LongKey;
                        break;
                    case ValType.String:
                        k = entry.StringKey;
                        break;
                    default:
                        k = _ser.OtherMessageFromProto(entry.OtherKey);
                        break;
                }

                if (entry.Value != null)
                {
                    var value = _ser.OtherMessageFromProto(entry.Value);
                    return (k, value);
                }

                return (k, default(TValue));
            }

            foreach (var entry in deltaGroup.Entries)
            {
                var underlying = FromProto(entry.Underlying);
                switch (entry.Operation)
                {
                    case ORMapDeltaOp.OrmapPut:
                        {
                            if (entry.EntryData.Count > 1)
                                throw new ArgumentOutOfRangeException(
                                    $"Can't deserialize key/value pair in ORDictionary delta - too many pairs on the wire");
                            var (key, value) = MapEntryFromProto(entry.EntryData[0]);

                            deltaOps.Add(new ORDictionary<TKey, TValue>.PutDeltaOperation(new ORSet<TKey>.AddDeltaOperation((ORSet<TKey>)underlying), (TKey)key, (TValue)value));
                        }
                        break;
                    case ORMapDeltaOp.OrmapRemove:
                        {
                            deltaOps.Add(new ORDictionary<TKey, TValue>.RemoveDeltaOperation(new ORSet<TKey>.RemoveDeltaOperation((ORSet<TKey>)underlying)));
                        }
                        break;
                    case ORMapDeltaOp.OrmapRemoveKey:
                        {
                            if (entry.EntryData.Count > 1)
                                throw new ArgumentOutOfRangeException(
                                    $"Can't deserialize key/value pair in ORDictionary delta - too many pairs on the wire");
                            var (key, value) = MapEntryFromProto(entry.EntryData[0]);
                            deltaOps.Add(new ORDictionary<TKey, TValue>.RemoveKeyDeltaOperation(new ORSet<TKey>.RemoveDeltaOperation((ORSet<TKey>)underlying), (TKey)key));
                        }
                        break;
                    case ORMapDeltaOp.OrmapUpdate:
                        {
                            var entries = entry.EntryData.Select(x => MapEntryFromProto(x))
                                .ToImmutableDictionary(x => (TKey)x.key, v => (IReplicatedData)v.value);
                            deltaOps.Add(new ORDictionary<TKey, TValue>.UpdateDeltaOperation(new ORSet<TKey>.AddDeltaOperation((ORSet<TKey>)underlying), entries));
                        }
                        break;
                    default:
                        throw new SerializationException($"Unknown ORDictionary delta operation ${entry.Operation}");
                }
            }

            return new ORDictionary<TKey, TValue>.DeltaGroup(deltaOps);
        }

        private ORDictionary.IDeltaGroupOp ORDictionaryDeltaGroupFromBinary(byte[] bytes)
        {
            var group = Proto.Msg.ORMapDeltaGroup.Parser.ParseFrom(bytes);
            return ORDictionaryDeltaGroupFromProto(group);
        }

        private ORDictionary.IPutDeltaOp ORDictionaryPutFromBinary(byte[] bytes)
        {
            var groupOp = ORDictionaryDeltaGroupFromBinary(bytes);
            if (groupOp.OperationsSerialization.Count == 1 &&
                groupOp.OperationsSerialization.First() is ORDictionary.IPutDeltaOp put)
                return put;
            throw new SerializationException($"Improper ORDictionary delta put operation size or kind");
        }

        private ORDictionary.IRemoveDeltaOp ORDictionaryRemoveFromBinary(byte[] bytes)
        {
            var groupOp = ORDictionaryDeltaGroupFromBinary(bytes);
            if (groupOp.OperationsSerialization.Count == 1 &&
                groupOp.OperationsSerialization.First() is ORDictionary.IRemoveDeltaOp remove)
                return remove;
            throw new SerializationException($"Improper ORDictionary delta remove operation size or kind");
        }

        private ORDictionary.IRemoveKeyDeltaOp ORDictionaryRemoveKeyFromBinary(byte[] bytes)
        {
            var groupOp = ORDictionaryDeltaGroupFromBinary(bytes);
            if (groupOp.OperationsSerialization.Count == 1 &&
                groupOp.OperationsSerialization.First() is ORDictionary.IRemoveKeyDeltaOp removeKey)
                return removeKey;
            throw new SerializationException($"Improper ORDictionary delta remove key operation size or kind");
        }

        private ORDictionary.IUpdateDeltaOp ORDictionaryUpdateFromBinary(byte[] bytes)
        {
            var groupOp = ORDictionaryDeltaGroupFromBinary(bytes);
            if (groupOp.OperationsSerialization.Count == 1 &&
                groupOp.OperationsSerialization.First() is ORDictionary.IUpdateDeltaOp update)
                return update;
            throw new SerializationException($"Improper ORDictionary delta update operation size or kind");
        }

        #endregion

        #region LWWDictionary

        private Proto.Msg.LWWMap ToProto(ILWWDictionary lwwDictionary)
        {
            var protoMaker = LWWDictProtoMaker.MakeGenericMethod(lwwDictionary.KeyType, lwwDictionary.ValueType);
            return (Proto.Msg.LWWMap)protoMaker.Invoke(this, new object[] { lwwDictionary });
        }

        private static readonly MethodInfo LWWDictProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(LWWDictToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.LWWMap LWWDictToProto<TKey, TValue>(ILWWDictionary o)
        {
            var lwwmap = (LWWDictionary<TKey, TValue>)o;
            var proto = new Proto.Msg.LWWMap();
            ToLWWMapEntries(lwwmap.Underlying.Entries, proto);
            proto.Keys = ToProto(lwwmap.Underlying.KeySet);
            proto.ValueTypeInfo = GetTypeDescriptor(typeof(TValue));
            return proto;
        }

        private void ToLWWMapEntries<TKey, TValue>(IImmutableDictionary<TKey, LWWRegister<TValue>> underlyingEntries, LWWMap proto)
        {
            var entries = new List<LWWMap.Types.Entry>();
            foreach (var e in underlyingEntries)
            {
                var thisEntry = new LWWMap.Types.Entry();
                switch (e.Key)
                {
                    case int i:
                        thisEntry.IntKey = i;
                        break;
                    case long l:
                        thisEntry.LongKey = l;
                        break;
                    case string str:
                        thisEntry.StringKey = str;
                        break;
                    default:
                        thisEntry.OtherKey = _ser.OtherMessageToProto(e.Key);
                        break;
                }

                thisEntry.Value = LWWToProto<TValue>(e.Value);
                entries.Add(thisEntry);
            }

            proto.Entries.Add(entries);
        }

        private static readonly MethodInfo LWWDictMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericLWWDictFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private ILWWDictionary LWWDictFromProto(Proto.Msg.LWWMap proto)
        {
            var keyType = GetTypeFromDescriptor(proto.Keys.TypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);

            var dictMaker = LWWDictMaker.MakeGenericMethod(keyType, valueType);
            return (ILWWDictionary)dictMaker.Invoke(this, new object[] { proto });
        }

        private ILWWDictionary GenericLWWDictFromProto<TKey, TValue>(Proto.Msg.LWWMap proto)
        {
            var keys = FromProto(proto.Keys);
            switch (proto.Keys.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.IntKey,
                            v => GenericLWWRegisterFromProto<TValue>(v.Value));
                        var orDict = new ORDictionary<int, LWWRegister<TValue>>((ORSet<int>)keys, entries);
                        return new LWWDictionary<int, TValue>(orDict);
                    }
                case ValType.Long:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.LongKey,
                            v => GenericLWWRegisterFromProto<TValue>(v.Value));
                        var orDict = new ORDictionary<long, LWWRegister<TValue>>((ORSet<long>)keys, entries);
                        return new LWWDictionary<long, TValue>(orDict);
                    }
                case ValType.String:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.StringKey,
                            v => GenericLWWRegisterFromProto<TValue>(v.Value));
                        var orDict = new ORDictionary<string, LWWRegister<TValue>>((ORSet<string>)keys, entries);
                        return new LWWDictionary<string, TValue>(orDict);
                    }
                default:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => (TKey)_ser.OtherMessageFromProto(x.OtherKey),
                            v => GenericLWWRegisterFromProto<TValue>(v.Value));
                        var orDict = new ORDictionary<TKey, LWWRegister<TValue>>((ORSet<TKey>)keys, entries);
                        return new LWWDictionary<TKey, TValue>(orDict);
                    }
            }
        }

        private ILWWDictionary LWWDictionaryFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.LWWMap.Parser.ParseFrom(bytes);
            return LWWDictFromProto(proto);
        }


        private object LWWDictionaryDeltaGroupFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.ORMapDeltaGroup.Parser.ParseFrom(bytes);
            var orDictOp = ORDictionaryDeltaGroupFromProto(proto);

            var orSetType = orDictOp.ValueType.GenericTypeArguments[0];
            var maker = LWWDictionaryDeltaMaker.MakeGenericMethod(orDictOp.KeyType, orSetType);
            return (ILWWDictionaryDeltaOperation)maker.Invoke(this, new object[] { orDictOp });
        }

        private static readonly MethodInfo LWWDictionaryDeltaMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(LWWDictionaryDeltaFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private ILWWDictionaryDeltaOperation LWWDictionaryDeltaFromProto<TKey, TValue>(ORDictionary.IDeltaOperation op)
        {
            var casted = (ORDictionary<TKey, LWWRegister<TValue>>.IDeltaOperation)op;
            return new LWWDictionary<TKey, TValue>.LWWDictionaryDelta(casted);
        }

        #endregion

        #region PNCounterDictionary

        private Proto.Msg.PNCounterMap ToProto(IPNCounterDictionary pnCounterDictionary)
        {
            var protoMaker = PNCounterDictProtoMaker.MakeGenericMethod(pnCounterDictionary.KeyType);
            return (Proto.Msg.PNCounterMap)protoMaker.Invoke(this, new object[] { pnCounterDictionary });
        }

        private static readonly MethodInfo PNCounterDictProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericPNCounterDictionaryToProto), BindingFlags.Instance | BindingFlags.NonPublic);


        private Proto.Msg.PNCounterMap GenericPNCounterDictionaryToProto<TKey>(IPNCounterDictionary pnCounterDictionary)
        {
            var pnDict = (PNCounterDictionary<TKey>)pnCounterDictionary;
            var proto = new Proto.Msg.PNCounterMap();
            proto.Keys = ToProto(pnDict.Underlying.KeySet);
            ToPNCounterEntries(pnDict.Underlying.Entries, proto);
            return proto;
        }

        private void ToPNCounterEntries<TKey>(IImmutableDictionary<TKey, PNCounter> underlyingEntries, PNCounterMap proto)
        {
            var entries = new List<PNCounterMap.Types.Entry>();
            foreach (var e in underlyingEntries)
            {
                var thisEntry = new PNCounterMap.Types.Entry();
                switch (e.Key)
                {
                    case int i:
                        thisEntry.IntKey = i;
                        break;
                    case long l:
                        thisEntry.LongKey = l;
                        break;
                    case string str:
                        thisEntry.StringKey = str;
                        break;
                    default:
                        thisEntry.OtherKey = _ser.OtherMessageToProto(e.Key);
                        break;
                }

                thisEntry.Value = ToProto(e.Value);
                entries.Add(thisEntry);
            }

            proto.Entries.Add(entries);
        }

        private IPNCounterDictionary PNCounterDictionaryFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.PNCounterMap.Parser.ParseFrom(bytes);
            return PNCounterDictionaryFromProto(proto);
        }

        private IPNCounterDictionary PNCounterDictionaryFromProto(Proto.Msg.PNCounterMap proto)
        {
            var keyType = GetTypeFromDescriptor(proto.Keys.TypeInfo);
            var dictMaker = PNCounterDictMaker.MakeGenericMethod(keyType);
            return (IPNCounterDictionary)dictMaker.Invoke(this, new object[] { proto });
        }

        private static readonly MethodInfo PNCounterDictMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericPNCounterDictionaryFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private IPNCounterDictionary GenericPNCounterDictionaryFromProto<TKey>(Proto.Msg.PNCounterMap proto)
        {
            var keys = FromProto(proto.Keys);
            switch (proto.Keys.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.IntKey,
                            v => PNCounterFromProto(v.Value));
                        var orDict = new ORDictionary<int, PNCounter>((ORSet<int>)keys, entries);
                        return new PNCounterDictionary<int>(orDict);
                    }
                case ValType.Long:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.LongKey,
                            v => PNCounterFromProto(v.Value));
                        var orDict = new ORDictionary<long, PNCounter>((ORSet<long>)keys, entries);
                        return new PNCounterDictionary<long>(orDict);
                    }
                case ValType.String:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.StringKey,
                            v => PNCounterFromProto(v.Value));
                        var orDict = new ORDictionary<string, PNCounter>((ORSet<string>)keys, entries);
                        return new PNCounterDictionary<string>(orDict);
                    }
                default:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => (TKey)_ser.OtherMessageFromProto(x.OtherKey),
                            v => PNCounterFromProto(v.Value));
                        var orDict = new ORDictionary<TKey, PNCounter>((ORSet<TKey>)keys, entries);
                        return new PNCounterDictionary<TKey>(orDict);
                    }
            }
        }

        private IPNCounterDictionaryDeltaOperation PNCounterDeltaFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.ORMapDeltaGroup.Parser.ParseFrom(bytes);
            var orDictOp = ORDictionaryDeltaGroupFromProto(proto);
            var maker = PNCounterDeltaMaker.MakeGenericMethod(orDictOp.KeyType);
            return (IPNCounterDictionaryDeltaOperation)maker.Invoke(this, new object[] { orDictOp });
        }

        private static readonly MethodInfo PNCounterDeltaMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(PNCounterDeltaFromProto), BindingFlags.Instance | BindingFlags.NonPublic);


        private IPNCounterDictionaryDeltaOperation PNCounterDeltaFromProto<TKey>(ORDictionary.IDeltaOperation op)
        {
            var casted = (ORDictionary<TKey, PNCounter>.IDeltaOperation)op;
            return new PNCounterDictionary<TKey>.PNCounterDictionaryDelta(casted);
        }

        #endregion

        #region ORMultiDictionary

        private Proto.Msg.ORMultiMap ToProto(IORMultiValueDictionary multi)
        {
            var protoMaker = MultiMapProtoMaker.MakeGenericMethod(multi.KeyType, multi.ValueType);
            return (Proto.Msg.ORMultiMap)protoMaker.Invoke(this, new object[] { multi });
        }

        private static readonly MethodInfo MultiMapProtoMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(MultiMapToProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private Proto.Msg.ORMultiMapDelta ToProto(IORMultiValueDictionaryDeltaOperation op)
        {
            var d = new ORMultiMapDelta() { WithValueDeltas = op.WithValueDeltas };
            d.Delta = ToProto(op.Underlying);
            return d;
        }

        private Proto.Msg.ORMultiMap MultiMapToProto<TKey, TValue>(IORMultiValueDictionary multi)
        {
            var ormm = (ORMultiValueDictionary<TKey, TValue>)multi;
            var proto = new Proto.Msg.ORMultiMap();
            proto.ValueTypeInfo = GetTypeDescriptor(typeof(TValue));
            if (ormm.DeltaValues)
            {
                proto.WithValueDeltas = true;
            }

            proto.Keys = ToProto(ormm.Underlying.KeySet);
            ToORMultiMapEntries(ormm.Underlying.Entries, proto);
            return proto;
        }

        private void ToORMultiMapEntries<TKey, TValue>(IImmutableDictionary<TKey, ORSet<TValue>> underlyingEntries, ORMultiMap proto)
        {
            var entries = new List<ORMultiMap.Types.Entry>();
            foreach (var e in underlyingEntries)
            {
                var thisEntry = new ORMultiMap.Types.Entry();
                switch (e.Key)
                {
                    case int i:
                        thisEntry.IntKey = i;
                        break;
                    case long l:
                        thisEntry.LongKey = l;
                        break;
                    case string str:
                        thisEntry.StringKey = str;
                        break;
                    default:
                        thisEntry.OtherKey = _ser.OtherMessageToProto(e.Key);
                        break;
                }

                thisEntry.Value = ToProto(e.Value);
                entries.Add(thisEntry);
            }

            proto.Entries.Add(entries);
        }

        private IORMultiValueDictionary ORMultiDictionaryFromBinary(byte[] bytes)
        {
            var ormm = Proto.Msg.ORMultiMap.Parser.ParseFrom(bytes);
            return ORMultiDictionaryFromProto(ormm);
        }

        private IORMultiValueDictionary ORMultiDictionaryFromProto(ORMultiMap proto)
        {
            var keyType = GetTypeFromDescriptor(proto.Keys.TypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);

            var dictMaker = MultiDictMaker.MakeGenericMethod(keyType, valueType);
            return (IORMultiValueDictionary)dictMaker.Invoke(this, new object[] { proto });
        }

        private static readonly MethodInfo MultiDictMaker =
           typeof(ReplicatedDataSerializer).GetMethod(nameof(GenericORMultiDictionaryFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private IORMultiValueDictionary GenericORMultiDictionaryFromProto<TKey, TValue>(ORMultiMap proto)
        {
            var keys = FromProto(proto.Keys);
            switch (proto.Keys.TypeInfo.Type)
            {
                case ValType.Int:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.IntKey,
                            v => (ORSet<TValue>)FromProto(v.Value));
                        var orDict = new ORDictionary<int, ORSet<TValue>>((ORSet<int>)keys, entries);
                        return new ORMultiValueDictionary<int, TValue>(orDict, proto.WithValueDeltas);
                    }
                case ValType.Long:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.LongKey,
                            v => (ORSet<TValue>)FromProto(v.Value));
                        var orDict = new ORDictionary<long, ORSet<TValue>>((ORSet<long>)keys, entries);
                        return new ORMultiValueDictionary<long, TValue>(orDict, proto.WithValueDeltas);
                    }
                case ValType.String:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => x.StringKey,
                            v => (ORSet<TValue>)FromProto(v.Value));
                        var orDict = new ORDictionary<string, ORSet<TValue>>((ORSet<string>)keys, entries);
                        return new ORMultiValueDictionary<string, TValue>(orDict, proto.WithValueDeltas);
                    }
                default:
                    {
                        var entries = proto.Entries.ToImmutableDictionary(x => (TKey)_ser.OtherMessageFromProto(x.OtherKey),
                            v => (ORSet<TValue>)FromProto(v.Value));
                        var orDict = new ORDictionary<TKey, ORSet<TValue>>((ORSet<TKey>)keys, entries);
                        return new ORMultiValueDictionary<TKey, TValue>(orDict, proto.WithValueDeltas);
                    }
            }
        }

        private object ORMultiDictionaryDeltaFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.ORMultiMapDelta.Parser.ParseFrom(bytes);
            var orDictOp = ORDictionaryDeltaGroupFromProto(proto.Delta);

            var orSetType = orDictOp.ValueType.GenericTypeArguments[0];
            var maker = ORMultiDictionaryDeltaMaker.MakeGenericMethod(orDictOp.KeyType, orSetType);
            return (IORMultiValueDictionaryDeltaOperation)maker.Invoke(this, new object[] { orDictOp, proto.WithValueDeltas });
        }

        private static readonly MethodInfo ORMultiDictionaryDeltaMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ORMultiDictionaryDeltaFromProto), BindingFlags.Instance | BindingFlags.NonPublic);

        private IORMultiValueDictionaryDeltaOperation ORMultiDictionaryDeltaFromProto<TKey, TValue>(ORDictionary.IDeltaOperation op, bool withValueDeltas)
        {
            var casted = (ORDictionary<TKey, ORSet<TValue>>.IDeltaOperation)op;
            return new ORMultiValueDictionary<TKey, TValue>.ORMultiValueDictionaryDelta(casted, withValueDeltas);
        }

        #endregion

        #region Keys

        private Proto.Msg.Key ToProto(IKey key)
        {
            var p = new Proto.Msg.Key();
            p.KeyId = key.Id;
            switch (key)
            {
                case IORSetKey orkey:
                    {
                        p.KeyType = KeyType.OrsetKey;
                        p.KeyTypeInfo = GetTypeDescriptor(orkey.SetType);
                        return p;
                    }
                case IGSetKey gSetKey:
                    {
                        p.KeyType = KeyType.GsetKey;
                        p.KeyTypeInfo = GetTypeDescriptor(gSetKey.SetType);
                        return p;
                    }
                case GCounterKey gKey:
                    {
                        p.KeyType = KeyType.GcounterKey;
                        return p;
                    }
                case PNCounterKey pKey:
                    {
                        p.KeyType = KeyType.PncounterKey;
                        return p;
                    }
                case FlagKey flagKey:
                    {
                        p.KeyType = KeyType.FlagKey;
                        return p;
                    }
                case ILWWRegisterKey registerKey:
                    {
                        p.KeyType = KeyType.LwwregisterKey;
                        p.KeyTypeInfo = GetTypeDescriptor(registerKey.RegisterType);
                        return p;
                    }
                case IORDictionaryKey dictionaryKey:
                    {
                        p.KeyType = KeyType.OrmapKey;
                        p.KeyTypeInfo = GetTypeDescriptor(dictionaryKey.KeyType);
                        p.ValueTypeInfo = GetTypeDescriptor(dictionaryKey.ValueType);
                        return p;
                    }
                case ILWWDictionaryKey lwwDictKey:
                    {
                        p.KeyType = KeyType.LwwmapKey;
                        p.KeyTypeInfo = GetTypeDescriptor(lwwDictKey.KeyType);
                        p.ValueTypeInfo = GetTypeDescriptor(lwwDictKey.ValueType);
                        return p;
                    }
                case IPNCounterDictionaryKey pnDictKey:
                    {
                        p.KeyType = KeyType.PncounterMapKey;
                        p.KeyTypeInfo = GetTypeDescriptor(pnDictKey.KeyType);
                        return p;
                    }
                case IORMultiValueDictionaryKey orMultiKey:
                    {
                        p.KeyType = KeyType.OrmultiMapKey;
                        p.KeyTypeInfo = GetTypeDescriptor(orMultiKey.KeyType);
                        p.ValueTypeInfo = GetTypeDescriptor(orMultiKey.ValueType);
                        return p;
                    }
                default:
                    throw new SerializationException($"Unrecognized key type [{key}]");
            }
        }

        private Proto.Msg.Key KeyFromBinary(byte[] bytes)
        {
            return Proto.Msg.Key.Parser.ParseFrom(bytes);
        }

        private IKey ORSetKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var genType = typeof(ORSetKey<>).MakeGenericType(keyType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey GSetKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var genType = typeof(GSetKey<>).MakeGenericType(keyType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey LWWRegisterKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var genType = typeof(LWWRegisterKey<>).MakeGenericType(keyType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey GCounterKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            return new GCounterKey(proto.KeyId);
        }

        private IKey PNCounterKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            return new PNCounterKey(proto.KeyId);
        }

        private IKey FlagKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            return new FlagKey(proto.KeyId);
        }

        private IKey ORDictionaryKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);

            var genType = typeof(ORDictionaryKey<,>).MakeGenericType(keyType, valueType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey LWWDictionaryKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);

            var genType = typeof(LWWDictionaryKey<,>).MakeGenericType(keyType, valueType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey PNCounterDictionaryKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);

            var genType = typeof(PNCounterDictionaryKey<>).MakeGenericType(keyType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        private IKey ORMultiValueDictionaryKeyFromBinary(byte[] bytes)
        {
            var proto = KeyFromBinary(bytes);
            var keyType = GetTypeFromDescriptor(proto.KeyTypeInfo);
            var valueType = GetTypeFromDescriptor(proto.ValueTypeInfo);

            var genType = typeof(ORMultiValueDictionaryKey<,>).MakeGenericType(keyType, valueType);
            return (IKey)Activator.CreateInstance(genType, proto.KeyId);
        }

        #endregion
    }
}
