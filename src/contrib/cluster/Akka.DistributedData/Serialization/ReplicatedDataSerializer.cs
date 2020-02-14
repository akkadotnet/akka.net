//-----------------------------------------------------------------------
// <copyright file="ReplicatedDataSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Google.Protobuf;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Akka.DistributedData.Serialization.Proto.Msg;
using Akka.Util;


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
        private const string LWWMapKeyManifest = "i";
        private const string PNCounterMapManifest = "J";
        private const string PNCounterMapDeltaOperationManifest = "Jo";
        private const string PNCounterMapKeyManifest = "j";
        private const string ORMultiMapManifest = "K";
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
                case IORSet o: return ToProto(o).ToByteArray();
                default:
                    throw new ArgumentException($"Can't serialize object of type [{obj.GetType().FullName}] in [{GetType().FullName}]");
            }
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            switch (manifest)
            {
                case ORSetManifest: return FromProto(Proto.Msg.ORSet.Parser.ParseFrom(bytes));
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
                case IPNCounterDictionary _: return PNCounterMapManifest;
                case IPNCounterDictionaryDeltaOperation _: return PNCounterMapDeltaOperationManifest;
                case IORMultiValueDictionary _: return ORMultiMapManifest;
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

        private static Proto.Msg.ORSet ToProto<T>(ORSet<T> set)
        {
            var p = new Proto.Msg.ORSet();
            p.Vvector = SerializationSupport.VersionVectorToProto(set.VersionVector);
            p.Dots.Add(set.ElementsMap.Values.Select(SerializationSupport.VersionVectorToProto));
            p.TypeInfo = new TypeDescriptor();
            return p;
        }

        private Proto.Msg.ORSet ToProto(IORSet orset)
        {
            switch (orset)
            {
                case ORSet<int> ints:
                {
                    var p = ToProto(ints);
                    p.TypeInfo.Type = ValType.Int;
                    p.IntElements.Add(ints.Elements);
                    return p;
                }
                case ORSet<long> longs:
                {
                    var p = ToProto(longs);
                    p.TypeInfo.Type = ValType.Long;
                    p.LongElements.Add(longs.Elements);
                    return p;
                }
                case ORSet<string> strings:
                {
                    var p = ToProto(strings);
                    p.TypeInfo.Type = ValType.String;
                    p.StringElements.Add(strings.Elements);
                    return p;
                }
                case ORSet<IActorRef> refs:
                {
                    var p = ToProto(refs);
                    p.TypeInfo.Type = ValType.ActorRef;
                    p.ActorRefElements.Add(refs.Select(Akka.Serialization.Serialization.SerializedActorPath));
                    return p;
                }
                default: // unknown type
                    dynamic d = orset;
                    return ToBinary(d);
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

            // runtime type - enter horrible serialization shit
           
            var setContentType = Type.GetType(orset.TypeInfo.TypeName);

            var eOther = orset.OtherElements.Zip(dots,
                (i, versionVector) => (_ser.OtherMessageFromProto(i), versionVector))
                .ToImmutableDictionary(x => x.Item1, x => x.versionVector);

            var setType = ORSetMaker.MakeGenericMethod(setContentType);
            return (IORSet)setType.Invoke(this, new object[]{ eOther, vector });
        }

        private static readonly MethodInfo ORSetMaker =
            typeof(ReplicatedDataSerializer).GetMethod(nameof(ToGenericORSet), BindingFlags.Static | BindingFlags.NonPublic);

        private static ORSet<T> ToGenericORSet<T>(ImmutableDictionary<object, VersionVector> elems, VersionVector vector)
        {
            var finalInput = elems.ToImmutableDictionary(x => (T)x.Key, v => v.Value);

            return new ORSet<T>(finalInput, vector);
        }

        /// <summary>
        /// Called when we're serializing none of the standard object types
        /// </summary>
        private Proto.Msg.ORSet ToBinary<T>(ORSet<T> orset)
        {
            var p = ToProto(orset);
            p.TypeInfo.Type = ValType.Other;
            p.TypeInfo.TypeName = typeof(T).TypeQualifiedName();
            p.OtherElements.Add(orset.Elements.Select(x => _ser.OtherMessageToProto(x)));
            return p;
        }
    }
}