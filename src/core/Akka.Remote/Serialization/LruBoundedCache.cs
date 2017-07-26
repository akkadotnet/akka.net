using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Remote.Serialization
{
    /// <summary>
    /// INTERNAL API
    /// </summary>
    /// <remarks>
    /// Fast hash based on the 128 bit Xorshift128+ PRNG. Mixes in character bits into the random generator state.
    /// </remarks>
    public static class FastHash
    {
        public static int OfString(string s)
        {
            var chars = s.ToCharArray();
            var s0 = 391408L;
            var s1 = 601258L;
            var i = 0;
            unchecked
            {
                while (i < chars.Length)
                {
                    var x = s0 ^ chars[i]; // Mix character into PRNG state
                    var y = s1;

                    // Xorshift128+ round
                    s0 = y;
                    x ^= x << 23;
                    y ^= (y >> 26);
                    x ^= (x >> 17);
                    s1 = x ^ y;

                    i += 1;
                }

                return(int)((s0 + s1) & 0xFFFFFFFF);
            }
        }

        public static int OfStringFast(string s)
        {
            var len = s.Length;
            var s0 = 391408L;
            var s1 = 601258L;
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

    public abstract class LruBoundedCache<TKey, TValue> where TValue:class
    {
        protected LruBoundedCache(int capacity, int evictAgeThreshold)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be larger than zero.");
            if((capacity & (capacity - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be a power of two.");
            if (!(evictAgeThreshold <= capacity))
                throw new ArgumentOutOfRangeException(nameof(evictAgeThreshold),
                    "Age threshold must be less than capacity");
            Capacity = capacity;
            EvictAgeThreshold = evictAgeThreshold;

            _mask = Capacity - 1;
            _keys = new TKey[Capacity];
            _values = new TValue[Capacity];
            _hashes = new int[Capacity];
            _epochs = Enumerable.Repeat(_epoch - evictAgeThreshold, Capacity).ToArray();
        }

        public int Capacity { get; private set; }

        public int EvictAgeThreshold { get; private set; }

        private int _mask;

        // Practically guarantee an overflow
        private int _epoch = Int32.MaxValue - 1;

        private readonly TKey[] _keys;
        private readonly TValue[] _values;
        private readonly int[] _hashes;
        private readonly int[] _epochs;
        
    }
}
