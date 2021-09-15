//-----------------------------------------------------------------------
// <copyright file="LruBoundedCache.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>
    /// Fast hash based on the 128 bit Xorshift128+ PRNG. Mixes in character bits into the random generator state.
    /// </remarks>
    internal static class FastHash
    {
        /// <summary>
        /// Allocatey, but safe implementation of FastHash
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <returns>A 32-bit pseudo-random hash value.</returns>
        public static int OfString(string s)
        {
            return OfString(s.AsSpan());
        }

        /// <summary>
        /// Allocatey, but safe implementation of FastHash
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <returns>A 32-bit pseudo-random hash value.</returns>
        public static int OfString(ReadOnlySpan<char> s)
        {
            var len = s.Length;
            var s0 = 391408L; // seed value 1, DON'T CHANGE
            var s1 = 601258L; // seed value 2, DON'T CHANGE
            unchecked
            {
                for (var i = 0; i < len; i++)
                {
                    var x = s0 ^ s[i]; // Mix character into PRNG state
                    var y = s1;

                    // Xorshift128+ round
                    s0 = y;
                    x ^= x << 23;
                    y ^= (y >> 26);
                    x ^= (x >> 17);
                    s1 = x ^ y;
                }

                return (int)((s0 + s1) & 0xFFFFFFFF);
            }
        }

        /// <summary>
        /// Unsafe (uses pointer arithmetic) but faster, allocation-free implementation
        /// of FastHash
        /// </summary>
        /// <param name="s">The input string.</param>
        /// <returns>A 32-bit pseudo-random hash value.</returns>
        public static int OfStringFast(string s)
        {
            var len = s.Length;
            var s0 = 391408L; // seed value 1, DON'T CHANGE
            var s1 = 601258L; // seed value 2, DON'T CHANGE
            unsafe
            {
                fixed (char* p1 = s)
                {
                    unchecked
                    {
                        for (char* p2 = p1; p2 < p1 + len; p2++)
                        {
                            var x = s0 ^ *p2; // Mix character into PRNG state
                            var y = s1;

                            // Xorshift128+ round
                            s0 = y;
                            x ^= x << 23;
                            y ^= (y >> 26);
                            x ^= (x >> 17);
                            s1 = x ^ y;
                        }

                        return (int)((s0 + s1) & 0xFFFFFFFF);
                    }
                }
            }
        }


    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class FastHashComparer : IEqualityComparer<string>
    {
        public readonly static FastHashComparer Default = new FastHashComparer();

        public bool Equals(string x, string y)
        {
            return StringComparer.Ordinal.Equals(x, y);
        }

        public int GetHashCode(string s)
        {
            return FastHash.OfStringFast(s);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    internal sealed class CacheStatistics
    {
        public CacheStatistics(int entries, int maxProbeDistance, double averageProbeDistance)
        {
            Entries = entries;
            MaxProbeDistance = maxProbeDistance;
            AverageProbeDistance = averageProbeDistance;
        }

        public int Entries { get; }

        public int MaxProbeDistance { get; }

        public double AverageProbeDistance { get; }
    }



    /// <summary>
    /// INTERNAL API
    /// 
    /// This class is based on a Robin-Hood hashmap
    /// (http://www.sebastiansylvan.com/post/robin-hood-hashing-should-be-your-default-hash-table-implementation/)
    /// with backshift(http://codecapsule.com/2013/11/17/robin-hood-hashing-backward-shift-deletion/).
    /// The main modification compared to an RH hashmap is that it never grows the map (no rehashes) instead it is allowed
    /// to kick out entires that are considered old.The implementation tries to keep the map close to full, only evicting
    /// old entries when needed.
    /// </summary>
    /// <typeparam name="TKey">The type of key used by the hash.</typeparam>
    /// <typeparam name="TValue">The type of value used in the cache.</typeparam>
    internal abstract class LruBoundedCache<TKey, TValue> where TValue : class
    {
        protected LruBoundedCache(int capacity, int evictAgeThreshold, IEqualityComparer<TKey> keyComparer)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be larger than zero.");
            if ((capacity & (capacity - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
            if (!(evictAgeThreshold <= capacity))
                throw new ArgumentOutOfRangeException(nameof(evictAgeThreshold),
                    "Age threshold must be less than capacity");
            Capacity = capacity;
            EvictAgeThreshold = evictAgeThreshold;

            _keyComparer = keyComparer;
            _mask = Capacity - 1;
            _keys = new TKey[Capacity];
            _values = new TValue[Capacity];
            _hashes = new int[Capacity];
            _epochs = new int[Capacity];
            _epochs.AsSpan().Fill(_epoch - evictAgeThreshold);
        }

        public int Capacity { get; private set; }

        public int EvictAgeThreshold { get; private set; }

        private readonly int _mask;

        // Practically guarantee an overflow
        private int _epoch = int.MaxValue - 1;

        private readonly IEqualityComparer<TKey> _keyComparer;
        private readonly TKey[] _keys;
        private readonly TValue[] _values;
        private readonly int[] _hashes;
        private readonly int[] _epochs;

        public CacheStatistics Stats
        {
            get
            {
                var i = 0;
                var sum = 0;
                var count = 0;
                var max = 0;
                while (i < _hashes.Length)
                {
                    if (_values[i] != null)
                    {
                        var dist = ProbeDistanceOf(i);
                        sum += dist;
                        count += 1;
                        max = Math.Max(dist, max);
                    }
                    i += 1;
                }
                return new CacheStatistics(count, max, (double)sum / count);
            }
        }

        public TValue Get(TKey k)
        {
            var h = _keyComparer.GetHashCode(k);

            var position = h & _mask;
            var probeDistance = 0;

            while (true)
            {
                var otherProbeDistance = ProbeDistanceOf(position);
                if (_values[position] == null)
                    return null;
                if (probeDistance > otherProbeDistance)
                    return null;
                if (_hashes[position] == h && _keyComparer.Equals(k, _keys[position]))
                {
                    return _values[position];
                }
                position = (position + 1) & _mask;
                probeDistance++;
            }
        }

        public bool TryGet(TKey k, out TValue value)
        {
            var h = _keyComparer.GetHashCode(k);

            var position = h & _mask;
            var probeDistance = 0;

            while (true)
            {
                var otherProbeDistance = ProbeDistanceOf(position);
                if (_values[position] == null || probeDistance > otherProbeDistance)
                {
                    value = default;
                    return false;
                }
                if (_hashes[position] == h && _keyComparer.Equals(k, _keys[position]))
                {
                    value = _values[position];
                    return true;
                }
                position = (position + 1) & _mask;
                probeDistance++;
            }
        }

        public TValue GetOrCompute(TKey k)
        {
            TryGetOrCompute(k, out var value);
            return value;
        }

        public bool TryGetOrCompute(TKey k, out TValue value)
        {
            var h = _keyComparer.GetHashCode(k);
            unchecked { _epoch += 1; }

            var position = h & _mask;
            var probeDistance = 0;

            while (true)
            {
                if (_values[position] == null)
                {
                    value = Compute(k);
                    if (IsCacheable(value))
                    {
                        _keys[position] = k;
                        _values[position] = value;
                        _hashes[position] = h;
                        _epochs[position] = _epoch;
                    }
                    return false;
                }
                else
                {
                    var otherProbeDistance = ProbeDistanceOf(position);
                    // If probe distance of the element we try to get is larger than the current slot's, then the element cannot be in
                    // the table since because of the Robin-Hood property we would have swapped it with the current element.
                    if (probeDistance > otherProbeDistance)
                    {
                        value = Compute(k);
                        if (IsCacheable(value))
                            Move(position, k, h, value, _epoch, probeDistance);
                        return false;
                    }
                    else if (_hashes[position] == h && _keyComparer.Equals(k, _keys[position]))
                    {
                        // Update usage
                        _epochs[position] = _epoch;
                        value = _values[position];
                        return true;
                    }
                    else
                    {
                        // This is not our slot yet
                        position = (position + 1) & _mask;
                        probeDistance++;
                    }
                }
            }
        }

        public bool TrySet(TKey key, TValue value)
        {
            if (!IsCacheable(value)) return false;

            var h = _keyComparer.GetHashCode(key);
            unchecked { _epoch += 1; }

            var position = h & _mask;
            var probeDistance = 0;

            while (true)
            {
                if (_values[position] == null)
                {
                    _keys[position] = key;
                    _values[position] = value;
                    _hashes[position] = h;
                    _epochs[position] = _epoch;
                    return true;
                }
                else
                {
                    var otherProbeDistance = ProbeDistanceOf(position);
                    // If probe distance of the element we try to get is larger than the current slot's, then the element cannot be in
                    // the table since because of the Robin-Hood property we would have swapped it with the current element.
                    if (probeDistance > otherProbeDistance)
                    {
                        Move(position, key, h, value, _epoch, probeDistance);
                        return true;
                    }
                    else if (_hashes[position] == h && _keyComparer.Equals(key, _keys[position]))
                    {
                        // Update usage
                        _epochs[position] = _epoch;
                        _values[position] = value;
                        return true;
                    }
                    else
                    {
                        // This is not our slot yet
                        position = (position + 1) & _mask;
                        probeDistance++;
                    }
                }
            }
        }

        private void RemoveAt(int position)
        {
            while (true)
            {
                var next = (position + 1) & _mask;
                if (_values[next] == null || ProbeDistanceOf(next) == 0)
                {
                    // Next is not movable, just empty this slot
                    _values[position] = null;
                }
                else
                {
                    // Shift the next slot here
                    _keys[position] = _keys[next];
                    _values[position] = _values[next];
                    _hashes[position] = _hashes[next];
                    _epochs[position] = _epochs[next];

                    // remove the shifted slot
                    position = next;
                    continue;
                }
                break;
            }
        }

        private void Move(int position, TKey k, int h, TValue value, int elemEpoch, int probeDistance)
        {
            if (_values[position] == null)
            {
                // Found an empty place, done
                _keys[position] = k;
                _values[position] = value;
                _hashes[position] = h;
                _epochs[position] = elemEpoch; // Do NOT update the epoch of the elem. It was not touched, just moved
            }
            else
            {
                var otherEpoch = _epochs[position];
                // Check if the current entry is too old
                if (_epoch - otherEpoch >= EvictAgeThreshold)
                {
                    // Remove the old entry to make space
                    RemoveAt(position);

                    // Try to insert our element in hand to its ideal slot
                    Move(h & _mask, k, h, value, elemEpoch, 0);
                }
                else
                {
                    var otherProbeDistance = ProbeDistanceOf(position);

                    // Check whose probe distance is larger. The one with the larger one wins the slot.
                    if (probeDistance > otherProbeDistance)
                    {
                        // Due to the Robin-Hood property, we now take away this slot from the "richer" and take it for ourselves
                        var otherKey = _keys[position];
                        var otherValue = _values[position];
                        var otherHash = _hashes[position];

                        _keys[position] = k;
                        _values[position] = value;
                        _hashes[position] = h;
                        _epochs[position] = elemEpoch;

                        // Move out the old one
                        Move((position + 1) & _mask, otherKey, otherHash, otherValue, otherEpoch,
                            otherProbeDistance + 1);
                    }
                    else
                    {
                        // We are the "richer" so we need to find another slot
                        Move((position + 1) & _mask, k, h, value, elemEpoch,
                            probeDistance + 1);
                    }
                }
            }
        }

        protected int ProbeDistanceOf(int slot)
        {
            return ProbeDistanceOf(_hashes[slot] & _mask, slot);
        }

        protected int ProbeDistanceOf(int idealSlot, int actualSlot)
        {
            return ((actualSlot - idealSlot) + Capacity) & _mask;
        }

        protected abstract TValue Compute(TKey k);

        protected abstract bool IsCacheable(TValue v);

        public override string ToString()
        {
            return $"LruBoundedCache(values = [{string.Join<TValue>(",", _values)}], hashes = [{string.Join(",", _hashes)}], " +
                   $"epochs = [{string.Join(",", _epochs)}], distances = [{Enumerable.Range(0, _hashes.Length).Select(x => ProbeDistanceOf(x))}]," +
                   $"epoch = {_epoch})";
        }
    }
}
