//-----------------------------------------------------------------------
// <copyright file="ReplicatorMessageSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using Akka.Actor;
using Akka.DistributedData.Internal;
using Akka.Util;
using Akka.Util.Internal;
using Hyperion;
using Serializer = Akka.Serialization.Serializer;

namespace Akka.DistributedData.Serialization
{
    public sealed class ReplicatorMessageSerializer : Serializer
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
        
        public static readonly Type WriteAckType = typeof(WriteAck);
        
        private readonly SmallCache<Read, byte[]> readCache;
        private readonly SmallCache<Write, byte[]> writeCache;
        private readonly Hyperion.Serializer serializer;
        private readonly byte[] writeAckBytes;

        public ReplicatorMessageSerializer(ExtendedActorSystem system) : base(system)
        {
            var cacheTtl = system.Settings.Config.GetTimeSpan("akka.cluster.distributed-data.serializer-cache-time-to-live");
            readCache = new SmallCache<Read, byte[]>(4, cacheTtl, Serialize);
            writeCache = new SmallCache<Write, byte[]>(4, cacheTtl, Serialize);

            var akkaSurrogate =
                Hyperion.Surrogate.Create<ISurrogated, ISurrogate>(
                    toSurrogate: from => from.ToSurrogate(system),
                    fromSurrogate: to => to.FromSurrogate(system));

            serializer = new Hyperion.Serializer(new SerializerOptions(
                preserveObjectReferences: true,
                versionTolerance: true,
                surrogates: new[] { akkaSurrogate },
                knownTypes: new []
                {
                    typeof(Get),
                    typeof(GetSuccess),
                    typeof(GetFailure),
                    typeof(NotFound),
                    typeof(Subscribe),
                    typeof(Unsubscribe),
                    typeof(Changed),
                    typeof(DataEnvelope),
                    typeof(Write),
                    typeof(WriteAck),
                    typeof(Read),
                    typeof(ReadResult),
                    typeof(Internal.Status),
                    typeof(Gossip)
                }));

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

        public override bool IncludeManifest => false;
        public override byte[] ToBinary(object obj)
        {
            if (obj is Write) return writeCache.GetOrAdd((Write) obj);
            if (obj is Read) return readCache.GetOrAdd((Read)obj);
            if (obj is WriteAck) return writeAckBytes;
            
            return Serialize(obj);
        }

        private byte[] Serialize(object obj)
        {
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(obj, stream);
                stream.Position = 0;
                return stream.ToArray();
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == WriteAckType) return WriteAck.Instance;
            using (var stream = new MemoryStream(bytes))
            {
                return serializer.Deserialize(stream);
            }
        }
    }
}