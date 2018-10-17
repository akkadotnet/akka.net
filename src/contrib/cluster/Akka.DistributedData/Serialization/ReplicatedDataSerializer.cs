//-----------------------------------------------------------------------
// <copyright file="ReplicatedDataSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.DistributedData.Internal;
using Akka.Serialization;
using Google.Protobuf;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akka.DistributedData.Serialization
{
    public sealed class ReplicatedDataSerializer : SerializerWithStringManifest, IWithSerializationSupport
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
        private const string PNCounterMapKeyManifest = "j";
        private const string ORMultiMapManifest = "K";
        private const string ORMultiMapKeyManifest = "k";
        private const string VersionVectorManifest = "L";

        private readonly Akka.Serialization.Serialization _serialization;
        private string _protocol;
        public string Protocol
        {
            get
            {
                var p = Volatile.Read(ref _protocol);
                if (ReferenceEquals(p, null))
                {
                    p = system.Provider.DefaultAddress.Protocol;
                    Volatile.Write(ref _protocol, p);
                }

                return p;
            }
        }

        public Actor.ActorSystem System
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => system;
        }
        public Akka.Serialization.Serialization Serialization
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _serialization;
        }

        private readonly byte[] _emptyArray = new byte[0];

        public ReplicatedDataSerializer(ExtendedActorSystem system) : base(system)
        {
            _serialization = system.Serialization;
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case DeletedData _: return DeletedDataManifest;
                case VersionVector _: return VersionVectorManifest;
                case GCounter _: return GCounterManifest;
                case PNCounter _: return PNCounterManifest;
                case Flag _: return FlagManifest;
                case IReplicatedData _:
                    {
                        dynamic d = o;
                        return Manifest(d);
                    }
                case GCounterKey _: return GCounterKeyManifest;
                case PNCounterKey _: return PNCounterKeyManifest;
                case FlagKey _: return FlagKeyManifest;
                case IKey _:
                    {
                        dynamic d = o;
                        return Manifest(d);
                    }
                default: throw new ArgumentException($"Can't serialize object of type [{o.GetType().FullName}] in [{GetType().FullName}]");
            }
        }

        private static string Manifest<T>(ORSet<T> _) => ORSetManifest;
        private static string Manifest<T>(ORSet<T>.AddDeltaOperation _) => ORSetAddManifest;
        private static string Manifest<T>(ORSet<T>.RemoveDeltaOperation _) => ORSetRemoveManifest;
        private static string Manifest<T>(GSet<T> _) => GSetManifest;
        private static string Manifest<T>(LWWRegister<T> _) => LWWRegisterManifest;
        private static string Manifest<T>(ORSet<T>.DeltaGroup _) => ORSetDeltaGroupManifest;
        private static string Manifest<T>(ORSet<T>.FullStateDeltaOperation _) => ORSetFullManifest;
        private static string Manifest<TKey, TVal>(ORDictionary<TKey, TVal> _) where TVal: IReplicatedData<TVal> => ORMapManifest;
        private static string Manifest<TKey, TVal>(ORDictionary<TKey, TVal>.PutDeltaOperation _) where TVal : IReplicatedData<TVal> => ORMapPutManifest;
        private static string Manifest<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveDeltaOperation _) where TVal : IReplicatedData<TVal> => ORMapRemoveManifest;
        private static string Manifest<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveKeyDeltaOperation _) where TVal : IReplicatedData<TVal> => ORMapRemoveKeyManifest;
        private static string Manifest<TKey, TVal>(ORDictionary<TKey, TVal>.UpdateDeltaOperation _) where TVal : IReplicatedData<TVal> => ORMapUpdateManifest;
        private static string Manifest<TKey, TVal>(LWWDictionary<TKey, TVal> _) => LWWMapManifest;
        private static string Manifest<TKey>(PNCounterDictionary<TKey> _) => PNCounterMapManifest;
        private static string Manifest<TKey, TVal>(ORMultiValueDictionary<TKey, TVal> _) => ORMultiMapManifest;
        
        private static string Manifest<T>(ORSetKey<T> _) => ORSetKeyManifest;
        private static string Manifest<T>(GSetKey<T> _) => GSetKeyManifest;
        private static string Manifest<T>(PNCounterDictionaryKey<T> _) => PNCounterMapKeyManifest;
        private static string Manifest<TKey, TVal>(ORDictionaryKey<TKey, TVal> _) where TVal : IReplicatedData<TVal> => ORMapKeyManifest;
        private static string Manifest<TKey, TVal>(LWWDictionaryKey<TKey, TVal> _) => LWWMapKeyManifest;
        private static string Manifest<TKey, TVal>(ORMultiValueDictionaryKey<TKey, TVal> _) => ORMultiMapKeyManifest;


        public override byte[] ToBinary(object o)
        {
            switch (o)
            {
                case DeletedData _: return _emptyArray;
                case VersionVector _: return ((VersionVector)o).ToProto().ToByteArray();
                case GCounter _: return ToProto((GCounter)o).ToByteArray();
                case PNCounter _: return ToProto((PNCounter)o).ToByteArray();
                case Flag _: return ToProto((Flag)o).ToByteArray();
                    
                case IReplicatedData _:
                    {
                        dynamic d = o;
                        return ToBinary(d);
                    }
                default: throw new ArgumentException($"Can't serialize object of type [{o.GetType().FullName}] in [{GetType().FullName}]");
            }
        }

        #region serialize GSet
        
        private byte[] ToBinary(GSet<long> o) => ToProto(o).ToByteArray();
        private Proto.Msg.GSet ToProto(GSet<long> o)
        {
            var proto = new Proto.Msg.GSet();
            foreach (var e in o.Elements)
                proto.LongElements.Add(e);
            return proto;
        }

        private byte[] ToBinary(GSet<int> o) => ToProto(o).ToByteArray();
        private Proto.Msg.GSet ToProto(GSet<int> o)
        {
            var proto = new Proto.Msg.GSet();
            foreach (var e in o.Elements)
                proto.IntElements.Add(e);
            return proto;
        }

        private byte[] ToBinary(GSet<string> o) => ToProto(o).ToByteArray();
        private Proto.Msg.GSet ToProto(GSet<string> o)
        {
            var proto = new Proto.Msg.GSet();
            foreach (var e in o.Elements)
                proto.StringElements.Add(e);
            return proto;
        }

        private byte[] ToBinary(GSet<IActorRef> o) => ToProto(o).ToByteArray();
        private Proto.Msg.GSet ToProto(GSet<IActorRef> o)
        {
            var proto = new Proto.Msg.GSet();
            foreach (var e in o.Elements)
                proto.ActorRefElements.Add(Akka.Serialization.Serialization.SerializedActorPath(e));
            return proto;
        }

        private byte[] ToBinary<T>(GSet<T> o) => ToProto<T>(o).ToByteArray();
        private Proto.Msg.GSet ToProto<T>(GSet<T> o)
        {
            var proto = new Proto.Msg.GSet();
            foreach (object e in o.Elements)
                proto.OtherElements.Add(this.OtherMessageToProto(e));
            return proto;
        }

        #endregion

        #region serialize ORSet

        private byte[] ToBinary(ORSet<int> o) => ToProto(o).Compress();
        private Proto.Msg.ORSet ToProto(ORSet<int> o)
        {
            var proto = new Proto.Msg.ORSet
            {
                Vvector = o.VersionVector.ToProto()
            };

            foreach (var e in o.ElementsMap)
            {
                proto.IntElements.Add(e.Key);
                proto.Dots.Add(e.Value.ToProto());
            }
            return proto;
        }

        private byte[] ToBinary(ORSet<long> o) => ToProto(o).Compress();
        private Proto.Msg.ORSet ToProto(ORSet<long> o)
        {
            var proto = new Proto.Msg.ORSet
            {
                Vvector = o.VersionVector.ToProto()
            };

            foreach (var e in o.ElementsMap)
            {
                proto.LongElements.Add(e.Key);
                proto.Dots.Add(e.Value.ToProto());
            }
            return proto;
        }

        private byte[] ToBinary(ORSet<string> o) => ToProto(o).Compress();
        private Proto.Msg.ORSet ToProto(ORSet<string> o)
        {
            var proto = new Proto.Msg.ORSet
            {
                Vvector = o.VersionVector.ToProto()
            };

            foreach (var e in o.ElementsMap)
            {
                proto.StringElements.Add(e.Key);
                proto.Dots.Add(e.Value.ToProto());
            }
            return proto;
        }

        private byte[] ToBinary(ORSet<IActorRef> o) => ToProto(o).Compress();
        private Proto.Msg.ORSet ToProto(ORSet<IActorRef> o)
        {
            var proto = new Proto.Msg.ORSet
            {
                Vvector = o.VersionVector.ToProto()
            };

            foreach (var e in o.ElementsMap)
            {
                proto.ActorRefElements.Add(Akka.Serialization.Serialization.SerializedActorPath(e.Key));
                proto.Dots.Add(e.Value.ToProto());
            }
            return proto;
        }

        private byte[] ToBinary<T>(ORSet<T> o) => ToProto<T>(o).Compress();
        private Proto.Msg.ORSet ToProto<T>(ORSet<T> o)
        {
            var proto = new Proto.Msg.ORSet
            {
                Vvector = o.VersionVector.ToProto()
            };

            foreach (var e in o.ElementsMap)
            {
                proto.OtherElements.Add(this.OtherMessageToProto(e.Key));
                proto.Dots.Add(e.Value.ToProto());
            }
            return proto;
        }

        #endregion

        #region serialize ORSet.AddDeltaOp

        private byte[] ToBinary(ORSet<int>.AddDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<int>.AddDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<long>.AddDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<long>.AddDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<string>.AddDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<string>.AddDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<IActorRef>.AddDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<IActorRef>.AddDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary<T>(ORSet<T>.AddDeltaOperation o) => ToProto<T>(o).ToByteArray();
        private Proto.Msg.ORSet ToProto<T>(ORSet<T>.AddDeltaOperation o) => ToProto<T>(o.Underlying);
        #endregion

        #region serialize ORSet.RemoveDeltaOp

        private byte[] ToBinary(ORSet<int>.RemoveDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<int>.RemoveDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<long>.RemoveDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<long>.RemoveDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<string>.RemoveDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<string>.RemoveDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<IActorRef>.RemoveDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<IActorRef>.RemoveDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary<T>(ORSet<T>.RemoveDeltaOperation o) => ToProto<T>(o).ToByteArray();
        private Proto.Msg.ORSet ToProto<T>(ORSet<T>.RemoveDeltaOperation o) => ToProto<T>(o.Underlying);
        #endregion

        #region serialize ORSet.GroupDeltaOp

        private byte[] ToBinary(ORSet<int>.DeltaGroup o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSetDeltaGroup ToProto(ORSet<int>.DeltaGroup o)
        {
            var proto = new Proto.Msg.ORSetDeltaGroup();
            foreach (var delta in o.Operations)
            {
                switch (delta)
                {
                    case ORSet<int>.AddDeltaOperation add:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Add,
                            Underlying = ToProto(add.Underlying)
                        });
                        break;
                    case ORSet<int>.RemoveDeltaOperation rem:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Remove,
                            Underlying = ToProto(rem.Underlying)
                        });
                        break;
                    case ORSet<int>.FullStateDeltaOperation full:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Full,
                            Underlying = ToProto(full.Underlying)
                        });
                        break;
                    default: throw new ArgumentException($"{delta} should not be nested");
                }

            }
            return proto;
        }

        private byte[] ToBinary(ORSet<long>.DeltaGroup o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSetDeltaGroup ToProto(ORSet<long>.DeltaGroup o)
        {
            var proto = new Proto.Msg.ORSetDeltaGroup();
            foreach (var delta in o.Operations)
            {
                switch (delta)
                {
                    case ORSet<long>.AddDeltaOperation add:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Add,
                            Underlying = ToProto(add.Underlying)
                        });
                        break;
                    case ORSet<long>.RemoveDeltaOperation rem:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Remove,
                            Underlying = ToProto(rem.Underlying)
                        });
                        break;
                    case ORSet<long>.FullStateDeltaOperation full:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Full,
                            Underlying = ToProto(full.Underlying)
                        });
                        break;
                    default: throw new ArgumentException($"{delta} should not be nested");
                }

            }
            return proto;
        }

        private byte[] ToBinary(ORSet<string>.DeltaGroup o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSetDeltaGroup ToProto(ORSet<string>.DeltaGroup o)
        {
            var proto = new Proto.Msg.ORSetDeltaGroup();
            foreach (var delta in o.Operations)
            {
                switch (delta)
                {
                    case ORSet<string>.AddDeltaOperation add:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Add,
                            Underlying = ToProto(add.Underlying)
                        });
                        break;
                    case ORSet<string>.RemoveDeltaOperation rem:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Remove,
                            Underlying = ToProto(rem.Underlying)
                        });
                        break;
                    case ORSet<string>.FullStateDeltaOperation full:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Full,
                            Underlying = ToProto(full.Underlying)
                        });
                        break;
                    default: throw new ArgumentException($"{delta} should not be nested");
                }

            }
            return proto;
        }

        private byte[] ToBinary(ORSet<IActorRef>.DeltaGroup o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSetDeltaGroup ToProto(ORSet<IActorRef>.DeltaGroup o)
        {
            var proto = new Proto.Msg.ORSetDeltaGroup();
            foreach (var delta in o.Operations)
            {
                switch (delta)
                {
                    case ORSet<IActorRef>.AddDeltaOperation add:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Add,
                            Underlying = ToProto(add.Underlying)
                        });
                        break;
                    case ORSet<IActorRef>.RemoveDeltaOperation rem:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Remove,
                            Underlying = ToProto(rem.Underlying)
                        });
                        break;
                    case ORSet<IActorRef>.FullStateDeltaOperation full:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Full,
                            Underlying = ToProto(full.Underlying)
                        });
                        break;
                    default: throw new ArgumentException($"{delta} should not be nested");
                }

            }
            return proto;
        }

        private byte[] ToBinary<T>(ORSet<T>.DeltaGroup o) => ToProto<T>(o).ToByteArray();
        private IMessage ToProto<T>(ORSet<T>.DeltaGroup o)
        {
            var proto = new Proto.Msg.ORSetDeltaGroup();
            foreach (var delta in o.Operations)
            {
                switch (delta)
                {
                    case ORSet<T>.AddDeltaOperation add:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Add,
                            Underlying = (Proto.Msg.ORSet)ToProto<T>(add.Underlying)
                        });
                        break;
                    case ORSet<T>.RemoveDeltaOperation rem:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Remove,
                            Underlying = (Proto.Msg.ORSet)ToProto<T>(rem.Underlying)
                        });
                        break;
                    case ORSet<T>.FullStateDeltaOperation full:
                        proto.Entries.Add(new Proto.Msg.ORSetDeltaGroup.Types.Entry
                        {
                            Operation = Proto.Msg.ORSetDeltaOp.Full,
                            Underlying = (Proto.Msg.ORSet)ToProto<T>(full.Underlying)
                        });
                        break;
                    default: throw new ArgumentException($"{delta} should not be nested");
                }

            }
            return proto;
        }

        #endregion

        #region serialize ORSet.FullStateDeltaOp

        private byte[] ToBinary(ORSet<int>.FullStateDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<int>.FullStateDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<long>.FullStateDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<long>.FullStateDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<string>.FullStateDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<string>.FullStateDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary(ORSet<IActorRef>.FullStateDeltaOperation o) => ToProto(o).ToByteArray();
        private Proto.Msg.ORSet ToProto(ORSet<IActorRef>.FullStateDeltaOperation o) => ToProto(o.Underlying);
        private byte[] ToBinary<T>(ORSet<T>.FullStateDeltaOperation o) => ToProto<T>(o).ToByteArray();
        private Proto.Msg.ORSet ToProto<T>(ORSet<T>.FullStateDeltaOperation o) => ToProto<T>(o.Underlying);

        #endregion

        private byte[] ToBinary<T>(LWWRegister<T> o) => ToProto<T>(o).ToByteArray();
        private Proto.Msg.LWWRegister ToProto<T>(LWWRegister<T> o)
        {
            return new Proto.Msg.LWWRegister
            {
                Node = o.UpdatedBy.ToProto(),
                Timestamp = o.Timestamp,
                State = this.OtherMessageToProto(o.Value)
            };
        }

        #region serialize PNCounterDictionary

        private byte[] ToBinary(PNCounterDictionary<int> o) => ToProto(o).Compress();
        private Proto.Msg.PNCounterMap ToProto(PNCounterDictionary<int> o)
        {
            var proto = new Proto.Msg.PNCounterMap
            {
                Keys = ToProto(o.Underlying.KeySet)
            };

            foreach (var entry in o.Underlying.Entries)
            {
                proto.Entries.Add(new Proto.Msg.PNCounterMap.Types.Entry
                {
                    IntKey = entry.Key,
                    Value = ToProto(entry.Value)
                });
            }

            return proto;
        }

        private byte[] ToBinary(PNCounterDictionary<long> o) => ToProto(o).Compress();
        private Proto.Msg.PNCounterMap ToProto(PNCounterDictionary<long> o)
        {
            var proto = new Proto.Msg.PNCounterMap
            {
                Keys = ToProto(o.Underlying.KeySet)
            };

            foreach (var entry in o.Underlying.Entries)
            {
                proto.Entries.Add(new Proto.Msg.PNCounterMap.Types.Entry
                {
                    LongKey = entry.Key,
                    Value = ToProto(entry.Value)
                });
            }

            return proto;
        }

        private byte[] ToBinary(PNCounterDictionary<string> o) => ToProto(o).Compress();
        private Proto.Msg.PNCounterMap ToProto(PNCounterDictionary<string> o)
        {
            var proto = new Proto.Msg.PNCounterMap
            {
                Keys = ToProto(o.Underlying.KeySet)
            };

            foreach (var entry in o.Underlying.Entries)
            {
                proto.Entries.Add(new Proto.Msg.PNCounterMap.Types.Entry
                {
                    StringKey = entry.Key,
                    Value = ToProto(entry.Value)
                });
            }

            return proto;
        }

        private byte[] ToBinary<TKey>(PNCounterDictionary<TKey> o) => ToProto<TKey>(o).Compress();
        private Proto.Msg.PNCounterMap ToProto<TKey>(PNCounterDictionary<TKey> o)
        {
            var proto = new Proto.Msg.PNCounterMap
            {
                Keys = ToProto<TKey>(o.Underlying.KeySet)
            };

            foreach (var entry in o.Underlying.Entries)
            {
                proto.Entries.Add(new Proto.Msg.PNCounterMap.Types.Entry
                {
                    OtherKey = this.OtherMessageToProto(entry.Key),
                    Value = ToProto(entry.Value)
                });
            }

            return proto;
        }

        #endregion

        #region serialize ORDictionary 

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal> o) where TVal : IReplicatedData<TVal> => ToProto<TKey, TVal>(o).Compress();
        private Proto.Msg.ORMap ToProto<TKey, TVal>(ORDictionary<TKey, TVal> o) where TVal : IReplicatedData<TVal>
        {
            dynamic keySet = o.KeySet;
            var orset = ToProto(keySet);

            var proto = new Proto.Msg.ORMap
            {
                Keys = orset
            };

            foreach (var entry in o.ValueMap)
            {
                dynamic key = entry.Key;
                object value = entry.Value;
                proto.Entries.Add(ToORDictionaryEntry(key, value));
            }

            return proto;
        }
        private Proto.Msg.ORMap.Types.Entry ToORDictionaryEntry(int key, object value) => new Proto.Msg.ORMap.Types.Entry
        {
            Value = this.OtherMessageToProto(value),
            IntKey = key
        };
        private Proto.Msg.ORMap.Types.Entry ToORDictionaryEntry(long key, object value) => new Proto.Msg.ORMap.Types.Entry
        {
            Value = this.OtherMessageToProto(value),
            LongKey = key
        };
        private Proto.Msg.ORMap.Types.Entry ToORDictionaryEntry(string key, object value) => new Proto.Msg.ORMap.Types.Entry
        {
            Value = this.OtherMessageToProto(value),
            StringKey = key
        };
        private Proto.Msg.ORMap.Types.Entry ToORDictionaryEntry<T>(T key, object value) => new Proto.Msg.ORMap.Types.Entry
        {
            Value = this.OtherMessageToProto(value),
            OtherKey = this.OtherMessageToProto(key)
        };

        #endregion

        #region serialize ORDictionary.PutDeltaOp

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal>.PutDeltaOperation o) where TVal : IReplicatedData<TVal> => ToProto(o).ToByteArray();
        private IMessage ToProto<TKey, TVal>(ORDictionary<TKey, TVal>.PutDeltaOperation o) where TVal : IReplicatedData<TVal>
        {
            throw new NotImplementedException();
        }
        #endregion

        #region serialize ORDictionary.RemoveDeltaOp

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveDeltaOperation o) where TVal : IReplicatedData<TVal> => ToProto(o).ToByteArray();
        private IMessage ToProto<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveDeltaOperation o) where TVal : IReplicatedData<TVal>
        {
            throw new NotImplementedException();
        }

        #endregion

        #region serialize ORDictionary.RemoveKeyDeltaOp

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveKeyDeltaOperation o) where TVal : IReplicatedData<TVal> => ToProto(o).ToByteArray();
        private IMessage ToProto<TKey, TVal>(ORDictionary<TKey, TVal>.RemoveKeyDeltaOperation o) where TVal : IReplicatedData<TVal>
        {
            throw new NotImplementedException();
        }

        #endregion

        #region serialize ORDictionary.UpdateDeltaOp

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal>.UpdateDeltaOperation o) where TVal : IReplicatedData<TVal> => ToProto(o).ToByteArray();
        private IMessage ToProto<TKey, TVal>(ORDictionary<TKey, TVal>.UpdateDeltaOperation o) where TVal : IReplicatedData<TVal>
        {
            throw new NotImplementedException();
        }

        #endregion

        #region serialize ORDictionary.GroupDeltaOp

        private byte[] ToBinary<TKey, TVal>(ORDictionary<TKey, TVal>.DeltaGroup o) where TVal : IReplicatedData<TVal> => ToProto(o).ToByteArray();
        private IMessage ToProto<TKey, TVal>(ORDictionary<TKey, TVal>.DeltaGroup o) where TVal : IReplicatedData<TVal>
        {
            throw new NotImplementedException();
        }

        #endregion

        #region serialize LWWDictionary

        private byte[] ToBinary<TKey, TVal>(LWWDictionary<TKey, TVal> o) => ToProto(o).Compress();
        private Proto.Msg.LWWMap ToProto<TKey, TVal>(LWWDictionary<TKey, TVal> o)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region serialize ORMultiValueDictionary

        private byte[] ToBinary<TKey, TVal>(ORMultiValueDictionary<TKey, TVal> o) => ToProto(o).Compress();
        private Proto.Msg.ORMultiMap ToProto<TKey, TVal>(ORMultiValueDictionary<TKey, TVal> o)
        {
            throw new NotImplementedException();
        }

        #endregion

        private Proto.Msg.Flag ToProto(Flag o)
        {
            return new Proto.Msg.Flag
            {
                Enabled = o.Enabled
            };
        }

        private Proto.Msg.PNCounter ToProto(PNCounter o)
        {
            var proto = new Proto.Msg.PNCounter
            {
                Increments = ToProto(o.Increments),
                Decrements = ToProto(o.Decrements),
            };

            return proto;
        }

        private Proto.Msg.GCounter ToProto(GCounter o)
        {
            var proto = new Proto.Msg.GCounter();
            foreach (var entry in o.State)
            {
                proto.Entries.Add(new Proto.Msg.GCounter.Types.Entry
                {
                    Node = entry.Key.ToProto(),
                    Value = ByteString.CopyFrom(BitConverter.GetBytes(entry.Value))
                });
            }
            return proto;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            throw new NotImplementedException();
        }
    }
}
