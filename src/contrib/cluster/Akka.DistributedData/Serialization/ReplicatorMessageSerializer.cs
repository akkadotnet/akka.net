//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.DistributedData.Internal;
using Akka.Serialization;
using Akka.Util.Internal;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Akka.DistributedData.Serialization.Proto.Msg;
using DataEnvelope = Akka.DistributedData.Internal.DataEnvelope;
using DeltaPropagation = Akka.DistributedData.Internal.DeltaPropagation;
using Gossip = Akka.DistributedData.Internal.Gossip;
using Read = Akka.DistributedData.Internal.Read;
using ReadResult = Akka.DistributedData.Internal.ReadResult;
using Status = Akka.DistributedData.Internal.Status;
using UniqueAddress = Akka.Cluster.UniqueAddress;
using Write = Akka.DistributedData.Internal.Write;

namespace Akka.DistributedData.Serialization
{
    public sealed class ReplicatorMessageSerializer : SerializerWithStringManifest
    {
        #region internal classes

        private sealed class SmallCache<TKey, TVal>
            where TKey : class
            where TVal : class
        {
            private readonly TimeSpan _ttl;
            private readonly Func<TKey, TVal> _getOrAddFactory;
            private readonly AtomicCounter _n = new AtomicCounter(0);
            private readonly int _mask;
            private readonly KeyValuePair<TKey, TVal>[] _elements;

            private DateTime _lastUsed;

            public SmallCache(int capacity, TimeSpan ttl, Func<TKey, TVal> getOrAddFactory)
            {
                _mask = capacity - 1;
                if ((capacity & _mask) != 0) throw new ArgumentException("Capacity must be power of 2 and less than or equal 32", nameof(capacity));
                if (capacity > 32) throw new ArgumentException("Capacity must be less than or equal 32", nameof(capacity));

                _ttl = ttl;
                _getOrAddFactory = getOrAddFactory;
                _elements = new KeyValuePair<TKey, TVal>[capacity];
                _lastUsed = DateTime.UtcNow;
            }

            public TVal this[TKey key]
            {
                get { return Get(key, _n.Current); }
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
                var i = _n.IncrementAndGet();
                _elements[i & _mask] = entry;
                _lastUsed = DateTime.UtcNow;
            }

            public TVal GetOrAdd(TKey key)
            {
                var position = _n.Current;
                var c = Get(key, position);
                if (!ReferenceEquals(c, null)) return c;
                var b2 = _getOrAddFactory(key);
                if (position == _n.Current)
                {
                    // no change, add the new value
                    Add(key, b2);
                    return b2;
                }
                else
                {
                    // some other thread added, try one more time
                    // to reduce duplicates
                    var c2 = Get(key, _n.Current);
                    if (!ReferenceEquals(c2, null)) return c2;
                    else
                    {
                        Add(key, b2);
                        return b2;
                    }
                }
            }

            /// <summary>
            /// Remove all elements if the if cache has not been used within <see cref="_ttl"/>.
            /// </summary>
            public void Evict()
            {
                if (DateTime.UtcNow - _lastUsed > _ttl)
                {
                    _elements.Initialize();
                }
            }

            private TVal Get(TKey key, int startIndex)
            {
                var end = startIndex + _elements.Length;
                _lastUsed = DateTime.UtcNow;
                var i = startIndex;
                while (end - i == 0)
                {
                    var x = _elements[i & _mask];
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

        private readonly SerializationSupport _ser;

        private readonly SmallCache<Read, byte[]> _readCache;
        private readonly SmallCache<Write, byte[]> _writeCache;
        private readonly byte[] _empty = Array.Empty<byte>();

        public ReplicatorMessageSerializer(Akka.Actor.ExtendedActorSystem system) : base(system)
        {
           _ser = new SerializationSupport(system);
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
                case DataEnvelope envelope: return DataEnvelopeToProto(envelope).ToByteArray();
                case Write write: return _writeCache.GetOrAdd(write);
                case WriteAck _: return _empty;
                case Read read: return _readCache.GetOrAdd(read);
                case ReadResult result: return ReadResultToProto(result).ToByteArray();
                case DeltaPropagation propagation: return DeltaPropagationToProto(propagation).ToByteArray();
                case Status status: return StatusToProto(status).ToByteArray();
                case Get get: return GetToProto(get).ToByteArray();
                case GetSuccess success: return GetSuccessToProto(success).ToByteArray();
                case Durable.DurableDataEnvelope envelope: return DurableDataEnvelopeToProto(envelope).ToByteArray();
                case Changed changed: return ChangedToProto(changed).ToByteArray();
                case NotFound found: return NotFoundToProto(found).ToByteArray();
                case GetFailure failure: return GetFailureToProto(failure).ToByteArray();
                case Subscribe subscribe: return SubscribeToProto(subscribe).ToByteArray();
                case Unsubscribe unsubscribe: return UnsubscribeToProto(unsubscribe).ToByteArray();
                case Gossip gossip: return SerializationSupport.Compress(GossipToProto(gossip));
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

            if (gossip.ToSystemUid.HasValue)
            {
                proto.HasToSystemUid = true;
                proto.ToSystemUid = gossip.ToSystemUid.Value;
            }

            if (gossip.FromSystemUid.HasValue)
            {
                proto.HasFromSystemUid = true;
                proto.FromSystemUid = gossip.FromSystemUid.Value;
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

        private OtherMessage OtherMessageToProto(object msg)
        {
            return _ser.OtherMessageToProto(msg);
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
                Data = this.OtherMessageToProto(msg.Data)
            };
            // only keep the PruningPerformed entries
            foreach (var p in msg.DataEnvelope.Pruning)
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
                RemovedAddress = SerializationSupport.UniqueAddressToProto(addr)
            };
            switch (pruning)
            {
                case PruningPerformed performed:
                    proto.Performed = true;
                    proto.ObsoleteTime = performed.ObsoleteTime.Ticks;
                    break;
                case PruningInitialized init:
                    proto.Performed = false;
                    proto.OwnerAddress = SerializationSupport.UniqueAddressToProto(init.Owner);
                    foreach (var address in init.Seen)
                    {
                        proto.Seen.Add(SerializationSupport.AddressToProto(address));
                    }
                    break;
            }

            return proto;
        }

        private Proto.Msg.Changed ChangedToProto(Changed msg)
        {
            return new Proto.Msg.Changed
            {
                Key = this.OtherMessageToProto(msg.Key),
                Data = this.OtherMessageToProto(msg.Data)
            };
        }

        private Proto.Msg.GetSuccess GetSuccessToProto(GetSuccess msg)
        {
            var proto = new Proto.Msg.GetSuccess
            {
                Key = this.OtherMessageToProto(msg.Key),
                Data = this.OtherMessageToProto(msg.Data)
            };

            if (!ReferenceEquals(null, msg.Request))
                proto.Request = OtherMessageToProto(msg.Request);

            return proto;
        }

        private Proto.Msg.Get GetToProto(Get msg)
        {
            var timeoutInMilis = msg.Consistency.Timeout.TotalMilliseconds;
            if (timeoutInMilis > 0XFFFFFFFFL)
                throw new ArgumentOutOfRangeException("Timeouts must fit in a 32-bit unsigned int");

            var proto = new Proto.Msg.Get
            {
                Key = this.OtherMessageToProto(msg.Key),
                Timeout = (uint)timeoutInMilis
            };

            switch (msg.Consistency)
            {
                case ReadLocal _: proto.Consistency = 1; break;
                case ReadFrom r: proto.Consistency = r.N; break;
                case ReadMajority rm:
                    proto.Consistency = 0;
                    proto.ConsistencyMinCap = rm.MinCapacity;
                    break;
                case ReadAll _: proto.Consistency = -1; break;
            }

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

            if(status.ToSystemUid.HasValue)
            {
                proto.HasToSystemUid = true;
                proto.ToSystemUid = status.ToSystemUid.Value;
            }

            if(status.FromSystemUid.HasValue)
            {
                proto.HasFromSystemUid = true;
                proto.FromSystemUid = status.FromSystemUid.Value;
            }

            return proto;
        }

        private Proto.Msg.DeltaPropagation DeltaPropagationToProto(DeltaPropagation msg)
        {
            var proto = new Proto.Msg.DeltaPropagation
            {
                FromNode = SerializationSupport.UniqueAddressToProto(msg.FromNode)
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
                proto.DeltaVersions = SerializationSupport.VersionVectorToProto(msg.DeltaVersions);

            return proto;
        }

        private Proto.Msg.Write WriteToProto(Write write)
        {
            var proto = new Proto.Msg.Write
            {
                Key = write.Key,
                Envelope = DataEnvelopeToProto(write.Envelope),
            };

            if(!(write.FromNode is null))
                proto.FromNode = SerializationSupport.UniqueAddressToProto(write.FromNode);
            return proto;
        }

        private Proto.Msg.Read ReadToProto(Read read)
        {
            var proto = new Proto.Msg.Read
            {
                Key = read.Key
            };

            if(!(read.FromNode is null))
                proto.FromNode = SerializationSupport.UniqueAddressToProto(read.FromNode);
            return proto;
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
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

            return new Gossip(
                builder.ToImmutable(),
                proto.SendBack,
                proto.HasToSystemUid ? (long?)proto.ToSystemUid : null,
                proto.HasFromSystemUid ? (long?)proto.FromSystemUid : null);
        }

        private DataEnvelope DataEnvelopeFromProto(Proto.Msg.DataEnvelope proto)
        {
            var data = (IReplicatedData)_ser.OtherMessageFromProto(proto.Data);
            var pruning = PruningFromProto(proto.Pruning);
            var vvector =
                proto.DeltaVersions != null
                ? _ser.VersionVectorFromProto(proto.DeltaVersions)
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
                    var removed = _ser.UniqueAddressFromProto(entry.RemovedAddress);
                    if (entry.Performed)
                    {
                        builder.Add(removed, new PruningPerformed(new DateTime(entry.ObsoleteTime)));
                    }
                    else
                    {
                        var seen = new Actor.Address[entry.Seen.Count];
                        var i = 0;
                        foreach (var s in entry.Seen)
                        {
                            seen[i] = _ser.AddressFromProto(s);
                            i++;
                        }
                        builder.Add(removed, new PruningInitialized(_ser.UniqueAddressFromProto(entry.OwnerAddress), seen));
                    }
                }

                return builder.ToImmutable();

            }
        }

        private Unsubscribe UnsubscribeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Unsubscribe.Parser.ParseFrom(bytes);
            var actorRef = system.Provider.ResolveActorRef(proto.Ref);
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            return new Unsubscribe(key, actorRef);
        }

        private Subscribe SubscribeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Subscribe.Parser.ParseFrom(bytes);
            var actorRef = system.Provider.ResolveActorRef(proto.Ref);
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            return new Subscribe(key, actorRef);
        }

        private GetFailure GetFailureFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.GetFailure.Parser.ParseFrom(bytes);
            var request = proto.Request != null ? _ser.OtherMessageFromProto(proto.Request) : null;
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);

            return new GetFailure(key, request);
        }

        private NotFound NotFoundFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.NotFound.Parser.ParseFrom(bytes);
            var request = proto.Request != null ? _ser.OtherMessageFromProto(proto.Request) : null;
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            return new NotFound(key, request);
        }

        private Changed ChangedFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Changed.Parser.ParseFrom(bytes);
            var data = _ser.OtherMessageFromProto(proto.Data);
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            return new Changed(key, data);
        }

        private Durable.DurableDataEnvelope DurableDataEnvelopeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DurableDataEnvelope.Parser.ParseFrom(bytes);
            var data = (IReplicatedData)_ser.OtherMessageFromProto(proto.Data);
            var pruning = PruningFromProto(proto.Pruning);

            return new Durable.DurableDataEnvelope(new DataEnvelope(data, pruning));
        }

        private GetSuccess GetSuccessFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.GetSuccess.Parser.ParseFrom(bytes);
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            var data = (IReplicatedData)_ser.OtherMessageFromProto(proto.Data);
            var request = proto.Request != null
                ? _ser.OtherMessageFromProto(proto.Request)
                : null;

            return new GetSuccess(key, request, data);
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
            var key = (IKey)_ser.OtherMessageFromProto(proto.Key);
            var request = proto.Request != null ? _ser.OtherMessageFromProto(proto.Request) : null;
            var timeout = new TimeSpan(proto.Timeout * TimeSpan.TicksPerMillisecond);
            IReadConsistency consistency;
            switch (proto.Consistency)
            {
                case 0: consistency = new ReadMajority(timeout, proto.ConsistencyMinCap); break;
                case -1: consistency = new ReadAll(timeout); break;
                case 1: consistency = ReadLocal.Instance; break;
                default: consistency = new ReadFrom(proto.Consistency, timeout); break;
            }
            return new Get(key, consistency, request);
        }

        private Status StatusFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Status.Parser.ParseFrom(bytes);
            var builder = ImmutableDictionary<string, ByteString>.Empty.ToBuilder();

            foreach (var entry in proto.Entries)
            {
                builder.Add(entry.Key, entry.Digest);
            }

            return new Status(
                builder.ToImmutable(),
                (int)proto.Chunk,
                (int)proto.TotChunks,
                proto.HasToSystemUid ? (long?)proto.ToSystemUid : null,
                proto.HasFromSystemUid ? (long?)proto.FromSystemUid : null);
        }

        private DeltaPropagation DeltaPropagationFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DeltaPropagation.Parser.ParseFrom(bytes);
            var reply = proto.Reply;
            var fromNode = _ser.UniqueAddressFromProto(proto.FromNode);

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
            var fromNode = proto.FromNode != null ? _ser.UniqueAddressFromProto(proto.FromNode) : null;
            return new Read(proto.Key, fromNode);
        }

        private Write WriteFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Write.Parser.ParseFrom(bytes);
            var fromNode = proto.FromNode != null ? _ser.UniqueAddressFromProto(proto.FromNode) : null;
            return new Write(proto.Key, DataEnvelopeFromProto(proto.Envelope), fromNode);
        }

        private DataEnvelope DataEnvelopeFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.DataEnvelope.Parser.ParseFrom(bytes);
            return DataEnvelopeFromProto(proto);
        }
    }
}
