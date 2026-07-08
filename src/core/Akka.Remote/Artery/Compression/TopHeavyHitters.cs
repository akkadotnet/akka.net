//-----------------------------------------------------------------------
// <copyright file="TopHeavyHitters.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;

namespace Akka.Remote.Artery.Compression
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// The bounded set of the most-frequently-observed values -- the candidates a receiver turns into
    /// a compression table for a given origin. Ported (behavior) from Apache Pekko's
    /// <c>TopHeavyHitters</c> (Apache 2.0): it holds at most <see cref="Max"/> entries (default
    /// <see cref="DefaultMax"/> = 256), each with the estimated count that got it in; a newcomer is
    /// admitted only if the set has room or its count beats the current weakest member (which is then
    /// evicted).
    ///
    /// <para>
    /// The MVP keeps this simple and exact rather than porting Pekko's fixed open-addressing hash +
    /// min-heap: eviction scans for the minimum (O(n), n &lt;= 256), which is off the hot path (heavy
    /// hitters are updated only on sampled inbound messages). Not thread-safe; the owning per-origin
    /// inbound-compression state serializes access.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The tracked value type (an actor-path or manifest string in this port).</typeparam>
    internal sealed class TopHeavyHitters<T> where T : class
    {
        /// <summary>Default maximum number of heavy hitters retained -- matches Pekko's <c>compression.actor-refs.max</c> / <c>manifests.max</c> default.</summary>
        public const int DefaultMax = 256;

        private readonly Dictionary<T, long> _items;

        public TopHeavyHitters(int max = DefaultMax)
        {
            if (max < 0)
                throw new ArgumentOutOfRangeException(nameof(max), max, "TopHeavyHitters max must be non-negative (0 disables heavy-hitter tracking).");

            Max = max;
            _items = new Dictionary<T, long>(max);
        }

        /// <summary>Maximum number of heavy hitters retained. <c>0</c> disables tracking entirely (nothing is ever admitted).</summary>
        public int Max { get; }

        /// <summary>Number of heavy hitters currently tracked.</summary>
        public int Count => _items.Count;

        /// <summary>
        /// Offers <paramref name="item"/> with its current estimated <paramref name="count"/>. Returns
        /// <see langword="true"/> if the value is now tracked as a heavy hitter (freshly admitted, or
        /// already present with its count refreshed); <see langword="false"/> if it was rejected
        /// because the set is full and it did not beat the weakest current member.
        /// </summary>
        public bool Update(T item, long count)
        {
            if (Max == 0)
                return false;

            if (_items.ContainsKey(item))
            {
                _items[item] = count;
                return true;
            }

            if (_items.Count < Max)
            {
                _items[item] = count;
                return true;
            }

            var (weakestKey, weakestCount) = FindWeakest();
            if (weakestKey is not null && count > weakestCount)
            {
                _items.Remove(weakestKey);
                _items[item] = count;
                return true;
            }

            return false;
        }

        /// <summary>Whether <paramref name="item"/> is currently a tracked heavy hitter.</summary>
        public bool Contains(T item) => _items.ContainsKey(item);

        /// <summary>The current heavy hitters (unordered).</summary>
        public IReadOnlyCollection<T> Items => _items.Keys;

        /// <summary>Forgets all heavy hitters (e.g. after a table is built and the window rolls over).</summary>
        public void Clear() => _items.Clear();

        private (T? key, long count) FindWeakest()
        {
            T? weakestKey = null;
            var weakestCount = long.MaxValue;
            foreach (var kv in _items)
            {
                if (kv.Value < weakestCount)
                {
                    weakestCount = kv.Value;
                    weakestKey = kv.Key;
                }
            }

            return (weakestKey, weakestCount);
        }
    }
}
