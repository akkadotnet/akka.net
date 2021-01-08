//-----------------------------------------------------------------------
// <copyright file="SimpleDnsCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Util;

namespace Akka.IO
{
    /// <summary>
    /// TBD
    /// </summary>
    internal interface IPeriodicCacheCleanup
    {
        /// <summary>
        /// TBD
        /// </summary>
        void CleanUp();
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SimpleDnsCache : DnsBase, IPeriodicCacheCleanup
    {
        private readonly AtomicReference<Cache> _cache;
        private readonly long _ticksBase;

        /// <summary>
        /// TBD
        /// </summary>
        public SimpleDnsCache()
        {
            _cache = new AtomicReference<Cache>(new Cache(new SortedSet<ExpiryEntry>(new ExpiryEntryComparer()), new Dictionary<string, CacheEntry>(), Clock));
            _ticksBase = DateTime.Now.Ticks;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public override Dns.Resolved Cached(string name)
        {
            return _cache.Value.Get(name);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        protected virtual long Clock()
        {
            var now = DateTime.Now.Ticks;
            return now - _ticksBase < 0
                ? 0
                : (now - _ticksBase) / 10000;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="r">TBD</param>
        /// <param name="ttl">TBD</param>
        /// <returns>TBD</returns>
        internal void Put(Dns.Resolved r, long ttl)
        {
            var c = _cache.Value;
            if (!_cache.CompareAndSet(c, c.Put(r, ttl)))
                Put(r, ttl);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public void CleanUp()
        {
            var c = _cache.Value;
            if (!_cache.CompareAndSet(c, c.Cleanup()))
                CleanUp();
        }

        class Cache
        {
            private readonly SortedSet<ExpiryEntry> _queue;
            private readonly Dictionary<string, CacheEntry> _cache;
            private readonly Func<long> _clock;
            private readonly object _queueCleanupLock = new object();

            public Cache(SortedSet<ExpiryEntry> queue, Dictionary<string, CacheEntry> cache, Func<long> clock)
            {
                _queue = queue;
                _cache = cache;
                _clock = clock;
            }

            public Dns.Resolved Get(string name)
            {
                if (_cache.TryGetValue(name, out var e) && e.IsValid(_clock()))
                    return e.Answer;
                return null;
            }

            public Cache Put(Dns.Resolved answer, long ttl)
            {
                var until = _clock() + ttl;

                var cache = new Dictionary<string, CacheEntry>(_cache);

                cache[answer.Name] = new CacheEntry(answer, until);

                return new Cache(
                    queue: new SortedSet<ExpiryEntry>(_queue, new ExpiryEntryComparer()) { new ExpiryEntry(answer.Name, until) },
                    cache: cache,
                    clock: _clock); 
            }

            public Cache Cleanup()
            {
                lock (_queueCleanupLock)
                {
                    var now = _clock();
                    while (_queue.Any() && !_queue.First().IsValid(now))
                    {
                        var minEntry = _queue.First();
                        var name = minEntry.Name;
                        _queue.Remove(minEntry);

                        if (_cache.TryGetValue(name, out var cacheEntry) && !cacheEntry.IsValid(now))
                            _cache.Remove(name);
                    }
                }
                
                return new Cache(new SortedSet<ExpiryEntry>(), new Dictionary<string, CacheEntry>(_cache), _clock);
            }
        }

        class CacheEntry
        {
            public CacheEntry(Dns.Resolved answer, long until)
            {
                Answer = answer;
                Until = until;
            }

            public Dns.Resolved Answer { get; private set; }
            public long Until { get; private set; }

            public bool IsValid(long clock)
            {
                return clock < Until;
            }
        }

        class ExpiryEntry 
        {
            public ExpiryEntry(string name, long until)
            {
                Name = name;
                Until = until;
            }

            public string Name { get; private set; }
            public long Until { get; private set; }

            public bool IsValid(long clock)
            {
                return clock < Until;
            }
        }

        class ExpiryEntryComparer : IComparer<ExpiryEntry>
        {
            /// <inheritdoc/>
            public int Compare(ExpiryEntry x, ExpiryEntry y)
            {
                return x.Until.CompareTo(y.Until);
            }
        }
    }
}
