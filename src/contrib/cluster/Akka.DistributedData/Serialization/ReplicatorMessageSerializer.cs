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
using System.IO;
using Akka.DistributedData.Serialization.Proto.Msg;
using Google.Protobuf;
using DataEnvelope = Akka.DistributedData.Internal.DataEnvelope;
using DeltaPropagation = Akka.DistributedData.Internal.DeltaPropagation;
using DurableDataEnvelope = Akka.DistributedData.Durable.DurableDataEnvelope;
using Gossip = Akka.DistributedData.Internal.Gossip;
using Read = Akka.DistributedData.Internal.Read;
using ReadResult = Akka.DistributedData.Internal.ReadResult;
using Status = Akka.DistributedData.Internal.Status;
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

        private readonly SmallCache<Read, byte[]> readCache;
        private readonly SmallCache<Write, byte[]> writeCache;
        private readonly Hyperion.Serializer serializer;
        private readonly byte[] writeAckBytes;
        private readonly byte[] empty = new byte[0];

        public ReplicatorMessageSerializer(Akka.Actor.ExtendedActorSystem system) : base(system)
        {
            _serialization = system.Serialization;
            var cacheTtl = system.Settings.Config.GetTimeSpan("akka.cluster.distributed-data.serializer-cache-time-to-live");
            readCache = new SmallCache<Read, byte[]>(4, cacheTtl, m => ReadToProto(m).ToByteArray());
            writeCache = new SmallCache<Write, byte[]>(4, cacheTtl, m => WriteToProto(m).ToByteArray());

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(WriteAck.Instance, stream);
                stream.Position = 0;
                writeAckBytes = stream.ToArray();
            }

            system.Scheduler.Advanced.ScheduleRepeatedly(cacheTtl, new TimeSpan(cacheTtl.Ticks / 2), () =>
            {
                readCache.Evict();
                writeCache.Evict();
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
                case Write _: return writeCache.GetOrAdd((Write)obj);
                case WriteAck _: return writeAckBytes;
                case Read _: return readCache.GetOrAdd((Read)obj);
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
                case Gossip _: return Compress(GossipToProto((Gossip)obj));
                case WriteNack _: return empty;
                case DeltaNack _: return empty;

                default: throw new ArgumentException($"Can't serialize object of type [{obj.GetType().FullName}] using [{GetType().FullName}]");
            }
        }

        private byte[] Compress(IMessage msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Gossip GossipToProto(Gossip gossip)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Unsubscribe UnsubscribeToProto(Unsubscribe unsubscribe)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Subscribe SubscribeToProto(Subscribe msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.GetFailure GetFailureToProto(GetFailure msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.NotFound NotFoundToProto(NotFound msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.DurableDataEnvelope DurableDataEnvelopeToProto(DurableDataEnvelope msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Changed ChangedToProto(Changed msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.GetSuccess GetSuccessToProto(GetSuccess msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Get GetToProto(Get msg)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Status StatusToProto(Status status)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.DeltaPropagation DeltaPropagationToProto(DeltaPropagation deltaPropagation)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.ReadResult ReadResultToProto(ReadResult readResult)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.DataEnvelope DataEnvelopeToProto(DataEnvelope dataEnvelope)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Write WriteToProto(Write write)
        {
            throw new NotImplementedException();
        }

        private Proto.Msg.Read ReadToProto(Read read)
        {
            throw new NotImplementedException();
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
                case GossipManifest: return GossipFromBinary(Decompress(bytes));
                case WriteNackManifest: return WriteNack.Instance;
                case DeltaNackManifest: return DeltaNack.Instance;

                default: throw new ArgumentException($"Unimplemented deserialization of message with manifest '{manifest}' using [{GetType().FullName}]");
            }
        }

        private byte[] Decompress(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object GossipFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object UnsubscribeFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object SubscribeFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object GetFailureFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object NotFoundFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object ChangedFromBinary(byte[] bytes)
        {
            var proto = Proto.Msg.Changed.Parser.ParseFrom(bytes);
            dynamic data = OtherMessageFromProto(proto.Data);
            dynamic key = OtherMessageFromProto(proto.Key);
            return ChangedDynamic(data, key);
        }

        private Changed<T> ChangedDynamic<T>(T data, IKey<T> key) where T : IReplicatedData => new Changed<T>(key, data);

        private object OtherMessageFromProto(OtherMessage proto)
        {
            var manifest = proto.MessageManifest == null || proto.MessageManifest.IsEmpty
                ? string.Empty
                : proto.MessageManifest.ToStringUtf8();
            return _serialization.Deserialize(proto.EnclosedMessage.ToByteArray(), proto.SerializerId, manifest);
        }

        private object DurableDataEnvelopeFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object GetSuccessFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object ReadResultFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object GetFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object StatusFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object DeltaPropagationFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object ReadFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object WriteFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }

        private object DataEnvelopeFromBinary(byte[] bytes)
        {
            throw new NotImplementedException();
        }
    }
}
