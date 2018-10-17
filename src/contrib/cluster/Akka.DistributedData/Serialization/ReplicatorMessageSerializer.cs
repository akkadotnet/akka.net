//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.DistributedData.Internal;
using Akka.Serialization;
using Akka.Util.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using Akka.Util;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Akka.Cluster;
using System.Threading;
using System.Runtime.CompilerServices;

namespace Akka.DistributedData.Serialization
{
    public sealed class ReplicatorMessageSerializer : SerializerWithStringManifest, IWithSerializationSupport
    {
        #region internal classes

        private sealed class SmallCache<TKey, TVal>
            where TKey : class
            where TVal : class
        {
            private readonly TimeSpan ttl;
            private readonly Func<TKey, TVal> getOrAddFactory;
            private readonly AtomicCounter n = new AtomicCounter(0);
            private readonly int mask;
            private readonly KeyValuePair<TKey, TVal>[] elements;

            private DateTime lastUsed;

            public SmallCache(int capacity, TimeSpan ttl, Func<TKey, TVal> getOrAddFactory)
            {
                mask = capacity - 1;
                if ((capacity & mask) != 0) throw new ArgumentException("Capacity must be power of 2 and less than or equal 32", nameof(capacity));
                if (capacity > 32) throw new ArgumentException("Capacity must be less than or equal 32", nameof(capacity));

                this.ttl = ttl;
                this.getOrAddFactory = getOrAddFactory;
                this.elements = new KeyValuePair<TKey, TVal>[capacity];
                this.lastUsed = DateTime.UtcNow;
            }

            public TVal this[TKey key]
            {
                get { return Get(key, n.Current); }
                set { Add(key, value); }
            }

            /// <summary>
            /// Add value under specified key. Overrides existing entry.
            /// </summary>
            public void Add(TKey key, TVal value) => Add(new KeyValuePair<TKey, TVal>(key, value));

            /// <summary>
            /// Add an entry to the cache. Overrides existing entry.
            /// </summary>
            public void Add(KeyValuePair<TKey, TVal> entry)
            {
                var i = n.IncrementAndGet();
                elements[i & mask] = entry;
                lastUsed = DateTime.UtcNow;
            }

            public TVal GetOrAdd(TKey key)
            {
                var position = n.Current;
                var c = Get(key, position);
                if (!ReferenceEquals(c, null)) return c;
                var b2 = getOrAddFactory(key);
                if (position == n.Current)
                {
                    // no change, add the new value
                    Add(key, b2);
                    return b2;
                }
                else
                {
                    // some other thread added, try one more time
                    // to reduce duplicates
                    var c2 = Get(key, n.Current);
                    if (!ReferenceEquals(c2, null)) return c2;
                    else
                    {
                        Add(key, b2);
                        return b2;
                    }
                }
            }

            /// <summary>
            /// Remove all elements if the if cache has not been used within <see cref="ttl"/>.
            /// </summary>
            public void Evict()
            {
                if (DateTime.UtcNow - lastUsed > ttl)
                {
                    elements.Initialize();
                }
            }

            private TVal Get(TKey key, int startIndex)
            {
                var end = startIndex + elements.Length;
                lastUsed = DateTime.UtcNow;
                var i = startIndex;
                while (end - i == 0)
                {
                    var x = elements[i & mask];
                    if (x.Key != key) i++;
                    else return x.Value;
                }

                return null;
            }
        }

        #endregion

        private const string GetManifest = "A";
        private const string GetSuccessManifest = "B";
        private const string NotFoundManifest = "C";
        private const string GetFailureManifest = "D";
        private const string SubscribeManifest = "E";
        private const string UnsubscribeManifest = "F";
        private const string ChangedManifest = "G";
        private const string DataEnvelopeManifest = "H";
        private const string WriteManifest = "I";
        private const string WriteAckManifest = "J";
        private const string ReadManifest = "K";
        private const string ReadResultManifest = "L";
        private const string StatusManifest = "M";
        private const string GossipManifest = "N";
        private const string WriteNackManifest = "O";
        private const string DurableDataEnvelopeManifest = "P";
        private const string DeltaPropagationManifest = "Q";
        private const string DeltaNackManifest = "R";

        private readonly Akka.Serialization.Serialization _serialization;

        private readonly SmallCache<Read, byte[]> _readCache;
        private readonly SmallCache<Write, byte[]> _writeCache;
        private readonly byte[] _empty = new byte[0];

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

        public ReplicatorMessageSerializer(Akka.Actor.ExtendedActorSystem system) : base(system)
        {
            _serialization = system.Serialization;
            var cacheTtl = system.Settings.Config.GetTimeSpan("akka.cluster.distributed-data.serializer-cache-time-to-live");
            _readCache = new SmallCache<Read, byte[]>(4, cacheTtl, m => ReadToProto(m).ToByteArray());
            _writeCache = new SmallCache<Write, byte[]>(4, cacheTtl, m => WriteToProto(m).ToByteArray());
            
            system.Scheduler.Advanced.ScheduleRepeatedly(cacheTtl, new TimeSpan(cacheTtl.Ticks / 2), () =>
            {
                _readCache.Evict();
                _writeCache.Evict();
            });
        }

        public override string Manifest(object o)
        {
            switch (o)
            {
                case DataEnvelope _: return DataEnvelopeManifest;
                case Write _: return WriteManifest;
                case WriteAck _: return WriteAckManifest;
                case Read _: return ReadManifest;
                case ReadResult _: return ReadResultManifest;
                case DeltaPropagation _: return DeltaPropagationManifest;
                case Status _: return StatusManifest;
                case Get _: return GetManifest;
                case GetSuccess _: return GetSuccessManifest;
                case Durable.DurableDataEnvelope _: return DurableDataEnvelopeManifest;
                case Changed _: return ChangedManifest;
                case NotFound _: return NotFoundManifest;
                case GetFailure _: return GetFailureManifest;
                case Subscribe _: return SubscribeManifest;
                case Unsubscribe _: return UnsubscribeManifest;
                case Gossip _: return GossipManifest;
                case WriteNack _: return WriteNackManifest;
                case DeltaNack _: return DeltaNackManifest;

                default: throw new ArgumentException($"Can't serialize object of type [{o.GetType().FullName}] using [{GetType().FullName}]");
            }
        }

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case DataEnvelope _: return DataEnvelopeToProto((DataEnvelope)obj).ToByteArray();
                case Write _: return _writeCache.GetOrAdd((Write)obj);
                case WriteAck _: return _empty;
                case Read _: return _readCache.GetOrAdd((Read)obj);
                case ReadResult _: return ReadResultToProto((ReadResult)obj).ToByteArray();
                case DeltaPropagation _: return DeltaPropagationToProto((DeltaPropagation)obj).ToByteArray();
                case Status _: return StatusToProto((Status)obj).ToByteArray();
                case Get _: return GetToProto((Get)obj).ToByteArray();
                case GetSuccess _: return GetSuccessToProto((GetSuccess)obj).ToByteArray();
                case Durable.DurableDataEnvelope _: return DurableDataEnvelopeToProto((Durable.DurableDataEnvelope)obj).ToByteArray();
                case Changed _: return ChangedToProto((Changed)obj).ToByteArray();
                case NotFound _: return NotFoundToProto((NotFound)obj).ToByteArray();
                case GetFailure _: return GetFailureToProto((GetFailure)obj).ToByteArray();
                case Subscribe _: return SubscribeToProto((Subscribe)obj).ToByteArray();
                case Unsubscribe _: return UnsubscribeToProto((Unsubscribe)obj).ToByteArray();
                case Gossip _: return GossipToProto((Gossip)obj).Compress();
                case WriteNack _: return _empty;
                case DeltaNack _: return _empty;

                default: throw new ArgumentException($"Can't serialize object of type [{obj.GetType().FullName}] using [{GetType().FullName}]");
            }
        }

        private Proto.Msg.Gossip GossipToProto(Gossip gossip)
        {
            var proto = new Proto.Msg.Gossip
            {
                SendBack = gossip.SendBack
            };

            foreach (var entry in gossip.UpdatedData)
            {
                proto.Entries.Add(new Proto.Msg.Gossip.Types.Entry
                {
                    Key = entry.Key,
                    Envelope = DataEnvelopeToProto(entry.Value)
                });
            }

            return proto;
        }

        private Proto.Msg.Unsubscribe UnsubscribeToProto(Unsubscribe unsubscribe)
        {
            return new Proto.Msg.Unsubscribe
            {
                Key = this.OtherMessageToProto(unsubscribe.Key),
                Ref = Akka.Serialization.Serialization.SerializedActorPath(unsubscribe.Subscriber)
            };
        }


        private Proto.Msg.Subscribe SubscribeToProto(Subscribe msg)
        {
            return new Proto.Msg.Subscribe
            {
                Key = this.OtherMessageToProto(msg.Key),
                Ref = Akka.Serialization.Serialization.SerializedActorPath(msg.Subscriber)
            };
        }

        private Proto.Msg.GetFailure GetFailureToProto(GetFailure msg)
        {
            var proto = new Proto.Msg.GetFailure
            {
                Key = this.OtherMessageToProto(msg.Key)
            };

            if (!ReferenceEquals(null, msg.Request))
                proto.Request = this.OtherMessageToProto(msg.Request);

            return proto;
        }

        private Proto.Msg.NotFound NotFoundToProto(NotFound msg)
        {
            var proto = new Proto.Msg.NotFound
            {
                Key = this.OtherMessageToProto(msg.Key)
            };

            if (!ReferenceEquals(null, msg.Request))
                proto.Request = this.OtherMessageToProto(msg.Request);

            return proto;
        }

        private Proto.Msg.DurableDataEnvelope DurableDataEnvelopeToProto(Durable.DurableDataEnvelope msg)
        {
            var proto = new Proto.Msg.DurableDataEnvelope
            {
                Data = this.OtherMessageToProto(msg.Data.Data)
            };
            // only keep the PruningPerformed entries
            foreach (var p in msg.Data.Pruning)
            {
                if (p.Value is PruningPerformed)
                    proto.Pruning.Add(PruningToProto(p.Key, p.Value));
            }

            return proto;
        }

        private Proto.Msg.DataEnvelope.Types.PruningEntry PruningToProto(UniqueAddress addr, IPruningState pruning)
        {
            var proto = new Proto.Msg.DataEnvelope.Types.PruningEntry
            {
                RemovedAddress = addr.ToProto()
            };
            switch (pruning)
            {
                case PruningPerformed performed:
                    proto.Performed = true;
                    proto.ObsoleteTime = performed.ObsoleteTime.Ticks / TimeSpan.TicksPerMillisecond;
                    break;
                case PruningInitialized init:
                    proto.Performed = false;
                    proto.OwnerAddress = init.Owner.ToProto();
                    foreach (var address in init.Seen)
                    {
                        proto.Seen.Add(address.ToProto());
                    }
                    break;
            }

            return proto;
        }

        private Proto.Msg.Changed ChangedToProto(Changed msg)
        {
            return new Proto.Msg.Changed
            {
                Key = this.OtherMessageToProto(msg),
                Data = this.OtherMessageToProto(msg)
            };
        }

        private Proto.Msg.GetSuccess GetSuccessToProto(GetSuccess msg)
        {
            var proto = new Proto.Msg.GetSuccess
            {
                Key = this.OtherMessageToProto(msg.Key),
                Data = this.OtherMessageToProto(msg.Key)
            };

            if (!ReferenceEquals(null, msg.Request))
                proto.Request = this.OtherMessageToProto(msg.Request);

            return proto;
        }

        private Proto.Msg.Get GetToProto(Get msg)
        {
            var consistencyValue = 1;
            switch (msg.Consistency)
            {
                case ReadLocal _: consistencyValue = 1; break;
                case ReadFrom r: consistencyValue = r.N; break;
                case ReadMajority _: consistencyValue = 0; break;
                case ReadAll _: consistencyValue = -1; break;
            }

            var proto = new Proto.Msg.Get
            {
                Key = this.OtherMessageToProto(msg.Key),
                Consistency = consistencyValue,
                Timeout = (uint) (msg.Consistency.Timeout.Ticks / TimeSpan.TicksPerMillisecond)
            };

            if (!ReferenceEquals(null, msg.Request))
                proto.Request = this.OtherMessageToProto(msg.Request);

            return proto;
        }

        private Proto.Msg.Status StatusToProto(Status status)
        {
            var proto = new Proto.Msg.Status
            {
                Chunk = (uint)status.Chunk,
                TotChunks = (uint)status.TotalChunks
            };

            foreach (var entry in status.Digests)
            {
                proto.Entries.Add(new Proto.Msg.Status.Types.Entry
                {
                    Key = entry.Key,
                    Digest = ByteString.CopyFrom(entry.Value.ToByteArray())
                });
            }

            return proto;
        }

        private Proto.Msg.DeltaPropagation DeltaPropagationToProto(DeltaPropagation msg)
        {
            var proto = new Proto.Msg.DeltaPropagation
            {
                FromNode = msg.FromNode.ToProto()
            };
            if (msg.ShouldReply)
                proto.Reply = msg.ShouldReply;

            foreach (var entry in msg.Deltas)
            {
                var d = entry.Value;
                var delta = new Proto.Msg.DeltaPropagation.Types.Entry
                {
                    Key = entry.Key,
                    FromSeqNr = d.FromSeqNr,
                    Envelope = DataEnvelopeToProto(d.DataEnvelope)
                };
                if (d.ToSeqNr != d.FromSeqNr)
                    delta.ToSeqNr = d.ToSeqNr;

                proto.Entries.Add(delta);
            }

            return proto;
        }

        private Proto.Msg.ReadResult ReadResultToProto(ReadResult msg)
        {
            var proto = new Proto.Msg.ReadResult();
            if (msg.Envelope != null)
                proto.Envelope = DataEnvelopeToProto(msg.Envelope);

            return proto;
        }

        private Proto.Msg.DataEnvelope DataEnvelopeToProto(DataEnvelope msg)
        {
            var proto = new Proto.Msg.DataEnvelope
            {
                Data = this.OtherMessageToProto(msg.Data)
            };

            foreach (var entry in msg.Pruning)
                proto.Pruning.Add(PruningToProto(entry.Key, entry.Value));

            if (!msg.DeltaVersions.IsEmpty)
                proto.DeltaVersions = msg.DeltaVersions.ToProto();

            return proto;
        }

        private Proto.Msg.Write WriteToProto(Write write)
        {
            return new Proto.Msg.Write
            {
                Key = write.Key,
                Envelope = DataEnvelopeToProto(write.Envelope)
            };
        }

        private Proto.Msg.Read ReadToProto(Read read)
        {
            return new Proto.Msg.Read
            {
                Key = read.Key
            };
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            dynamic d;
            switch (manifest)
            {
                case DataEnvelopeManifest: return DataEnvelopeFromBinary(bytes);
                case WriteManifest: return WriteFromBinary(bytes);
                case WriteAckManifest: return WriteAck.Instance;
                case ReadManifest: return ReadFromBinary(bytes);
                case ReadResultManifest: return ReadResultFromBinary(bytes);
                case DeltaPropagationManifest: return DeltaPropagationFromBinary(bytes);
                case StatusManifest: return StatusFromBinary(bytes);
                case GetManifest: return GetFromBinary(bytes);
                case GetSuccessManifest: return GetSuccessFromBinary(bytes);
                case DurableDataEnvelopeManifest: return DurableDataEnvelopeFromBinary(bytes);
                case ChangedManifest: return ChangedFromBinary(bytes);
                case NotFoundManifest: return NotFoundFromBinary(bytes);
                case GetFailureManifest: return GetFailureFromBinary(bytes);
                case SubscribeManifest: return SubscribeFromBinary(bytes);
                case UnsubscribeManifest: return UnsubscribeFromBinary(bytes);
                case GossipManifest: return GossipFromBinary(SerializationSupport.Decompress(bytes));
                case WriteNackManifest: return WriteNack.Instance;
                case DeltaNackManifest: return DeltaNack.Instance;

                default: throw new ArgumentException($"Unimplemented deserialization of message with manifest '{manifest}' using [{GetType().FullName}]");
            }
        }
        
        private Gossip GossipFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Gossip.Parser.ParseFrom(bytes);
            var builder = ImmutableDictionary<string, DataEnvelope>.Empty.ToBuilder();
            foreach (var entry in proto.Entries)
            {
                builder.Add(entry.Key, DataEnvelopeFromProto(entry.Envelope));
            }

            return new Gossip(builder.ToImmutable(), proto.SendBack);
        }

        private DataEnvelope DataEnvelopeFromProto(Proto.Msg.DataEnvelope proto)
        {
            var data = (IReplicatedData) this.OtherMessageFromProto(proto.Data);
            var pruning = PruningFromProto(proto.Pruning);
            var vvector =
                proto.DeltaVersions != null
                ? this.VersionVectorFromProto(proto.DeltaVersions)
                : VersionVector.Empty;

            return new DataEnvelope(data, pruning, vvector);
        }

        private ImmutableDictionary<UniqueAddress, IPruningState> PruningFromProto(RepeatedField<Proto.Msg.DataEnvelope.Types.PruningEntry> pruning)
        {
            if (pruning.Count == 0)
            {
                return ImmutableDictionary<UniqueAddress, IPruningState>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary<UniqueAddress, IPruningState>.Empty.ToBuilder();

                foreach (var entry in pruning)
                {
                    var removed = this.UniqueAddressFromProto(entry.RemovedAddress);
                    if (entry.Performed)
                    {
                        builder.Add(removed, new PruningPerformed(new DateTime(entry.ObsoleteTime * TimeSpan.TicksPerMillisecond)));
                    }
                    else
                    {
                        var seen = new Actor.Address[entry.Seen.Count];
                        var i = 0;
                        foreach (var s in entry.Seen)
                        {
                            seen[i] = this.AddressFromProto(s.Hostname, s.Port);
                            i++;
                        }
                        builder.Add(removed, new PruningInitialized(this.UniqueAddressFromProto(entry.OwnerAddress), seen));
                    }
                }

                return builder.ToImmutable();

            }
        }

        private Unsubscribe UnsubscribeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Unsubscribe.Parser.ParseFrom(bytes);
            var actorRef = system.Provider.ResolveActorRef(proto.Ref);
            dynamic key = this.OtherMessageFromProto(proto.Key);
            return DynamicUnsubscribe(key, actorRef);
        }

        private Unsubscribe DynamicUnsubscribe<T>(IKey<T> key, Actor.IActorRef actorRef) where T : IReplicatedData
        {
            return new Unsubscribe<T>(key, actorRef);
        }

        private Subscribe SubscribeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Subscribe.Parser.ParseFrom(bytes);
            var actorRef = system.Provider.ResolveActorRef(proto.Ref);
            dynamic key = this.OtherMessageFromProto(proto.Key);
            return DynamicSubscribe(key, actorRef);
        }

        private Subscribe DynamicSubscribe<T>(IKey<T> key, Actor.IActorRef actorRef) where T : IReplicatedData
        {
            return new Subscribe<T>(key, actorRef);
        }

        private GetFailure GetFailureFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.GetFailure.Parser.ParseFrom(bytes);
            var request = proto.Request != null ? this.OtherMessageFromProto(proto.Request) : null;
            dynamic key = this.OtherMessageFromProto(proto.Key);

            return DynamicGetFailure(key, request);
        }

        private GetFailure DynamicGetFailure<T>(dynamic key, object request) where T : IReplicatedData
        {
            return new GetFailure<T>(key, request);
        }

        private NotFound NotFoundFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.NotFound.Parser.ParseFrom(bytes);
            var request = proto.Request != null ? this.OtherMessageFromProto(proto.Request) : null;
            dynamic key = this.OtherMessageFromProto(proto.Key);
            return DynamicNotFound(key, request);
        }

        private NotFound DynamicNotFound<T>(IKey<T> key, object request) where T: IReplicatedData
        {
            return new NotFound<T>(key, request);
        }

        private Changed ChangedFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Changed.Parser.ParseFrom(bytes);
            dynamic data = this.OtherMessageFromProto(proto.Data);
            dynamic key = this.OtherMessageFromProto(proto.Key);
            return ChangedDynamic(data, key);
        }

        private Changed ChangedDynamic<T>(T data, IKey<T> key) where T : IReplicatedData => new Changed<T>(key, data);


        private Durable.DurableDataEnvelope DurableDataEnvelopeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DurableDataEnvelope.Parser.ParseFrom(bytes);
            var data = (IReplicatedData) this.OtherMessageFromProto(proto.Data);
            var pruning = PruningFromProto(proto.Pruning);

            return new Durable.DurableDataEnvelope(new DataEnvelope(data, pruning));
        }

        private GetSuccess GetSuccessFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.GetSuccess.Parser.ParseFrom(bytes);
            dynamic key = this.OtherMessageFromProto(proto.Key);
            dynamic data = this.OtherMessageFromProto(proto.Data);
            var request = proto.Request != null
                ? this.OtherMessageFromProto(proto.Request)
                : null;

            return DynamicGetSuccess(data, key, request);
        }

        private GetSuccess DynamicGetSuccess<T>(T data, IKey<T> key, object request) where T: IReplicatedData
        {
            return new GetSuccess<T>(key, request, data);
        }

        private ReadResult ReadResultFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.ReadResult.Parser.ParseFrom(bytes);
            var envelope = proto.Envelope != null
                ? DataEnvelopeFromProto(proto.Envelope)
                : null;
            return new ReadResult(envelope);
        }

        private Get GetFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Get.Parser.ParseFrom(bytes);
            dynamic key = this.OtherMessageFromProto(proto.Key);
            var request = proto.Request != null ? this.OtherMessageFromProto(proto.Request) : null;
            var timeout = new TimeSpan(proto.Timeout * TimeSpan.TicksPerMillisecond);
            IReadConsistency consistency;
            switch (proto.Consistency)
            {
                case 0: consistency = new ReadMajority(timeout); break;
                case -1: consistency = new ReadAll(timeout); break;
                case 1: consistency = ReadLocal.Instance; break;
                default: consistency = new ReadFrom(proto.Consistency, timeout); break;
            }
            return DynamicGet(key, consistency, request);
        }

        private Get<T> DynamicGet<T>(IKey<T> key, IReadConsistency consistency, object request) where T: IReplicatedData
        {
            return new Get<T>(key, consistency, request);
        }

        private Status StatusFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Status.Parser.ParseFrom(bytes);
            var builder = ImmutableDictionary<string, ByteString>.Empty.ToBuilder();

            foreach (var entry in proto.Entries)
            {
                builder.Add(entry.Key, entry.Digest);
            }

            return new Status(builder.ToImmutable(), (int)proto.Chunk, (int)proto.TotChunks);
        }

        private DeltaPropagation DeltaPropagationFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DeltaPropagation.Parser.ParseFrom(bytes);
            var reply = proto.Reply;
            var fromNode = this.UniqueAddressFromProto(proto.FromNode);

            var builder = ImmutableDictionary<string, Delta>.Empty.ToBuilder();
            foreach (var entry in proto.Entries)
            {
                var fromSeqNr = entry.FromSeqNr;
                var toSeqNr = entry.ToSeqNr == default(long) ? fromSeqNr : entry.ToSeqNr;
                builder.Add(entry.Key, new Delta(DataEnvelopeFromProto(entry.Envelope), fromSeqNr, toSeqNr));
            }

            return new DeltaPropagation(fromNode, reply, builder.ToImmutable());
        }

        private Read ReadFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Read.Parser.ParseFrom(bytes);
            return new Read(proto.Key);
        }

        private Write WriteFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Write.Parser.ParseFrom(bytes);
            return new Write(proto.Key, DataEnvelopeFromProto(proto.Envelope));
        }

        private DataEnvelope DataEnvelopeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DataEnvelope.Parser.ParseFrom(bytes);
            return DataEnvelopeFromProto(proto);
        }
    }
}
