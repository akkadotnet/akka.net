using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Akka.Remote.Artery.Utils;
using Akka;
using Akka.Actor;

namespace Akka.Remote.Artery.Compress
{
    /// <summary>
    /// INTERNAL API: Count-Min Sketch data structure.
    /// 
    /// Not thread-safe.
    ///
    /// An Improved Data Stream Summary: The Count-Min Sketch and its Applications
    /// https://web.archive.org/web/20060907232042/http://www.eecs.harvard.edu/~michaelm/CS222/countmin.pdf
    /// his implementation is mostly taken and adjusted from the Apache V2 licensed project
    /// stream-lib`, located here:
    /// https://github.com/clearspring/stream-lib/blob/master/src/main/java/com/clearspring/analytics/stream/frequency/CountMinSketch.java
    /// </summary>
    internal class CountMinSketch
    {
        private readonly int _depth;
        private readonly int _width;
        private readonly long[,] _table;

        private readonly int[] _recycleableCmsHashBuckets;

        // Referred to as epsilon in the whitepaper
        public double RelativeError { get; }

        public double Confidence { get; }

        public long Size { get; private set; }

        public CountMinSketch(int depth, int width, int seed)
        {
            if ((width & (width - 1)) != 0)
                throw new ArgumentException($"{nameof(width)} must be a power of 2, was: {width}");

            _depth = depth;
            _width = width;
            RelativeError = 2.0 / width;
            Confidence = 1 - 1 / Math.Pow(2, depth);
            _recycleableCmsHashBuckets = PreallocateHashBucketsArray(depth);
            _table = InitTablesWith(depth, width, seed);
        }

        private static long[,] InitTablesWith(int depth, int width, int seed)
            => new long[depth, width];

        private static int[] PreallocateHashBucketsArray(int depth)
            => new int[depth];

        /// <summary>
        /// Similar to <code>add</code>, however we reuse the fact that the hash buckets have to be calculated
        /// for <code>add</code> already, and a separate <code>estimateCount</code> operation would have to calculate
        /// them again, so we do it all in one go.
        /// </summary>
        /// <param name="item"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public long AddObjectAndEstimateCount(object item, long count)
        {
            if (count < 0)
            {
                // Actually for negative increments we'll need to use the median
                // instead of minimum, and accuracy will suffer somewhat.
                // Probably makes sense to add an "allow negative increments"
                // parameter to constructor.
                throw new ArgumentException("Negative increments not implemented.");
            }

            Murmur3.HashBuckets(item, _recycleableCmsHashBuckets, _width);
            for (var i = 0; i < _depth; ++i)
            {
                _table[i, _recycleableCmsHashBuckets[i]] += count;
            }

            Size += count;
            return EstimateCount(_recycleableCmsHashBuckets);
        }

        public long EstimateCount(object item)
        {
            Murmur3.HashBuckets(item, _recycleableCmsHashBuckets, _width);
            return EstimateCount(_recycleableCmsHashBuckets);
        }

        private long EstimateCount(int[] buckets)
        {
            var res = long.MaxValue;
            for (var i = 0; i < _depth; ++i)
            {
                res = Math.Min(res, _table[i, buckets[i]]);
            }

            return res;
        }

        /// <summary>
        /// Local implementation of murmur3 hash optimized to used in count min sketch
        ///
        /// Inspired by scala (scala.util.hashing.MurmurHash3) and C port of MurmurHash3
        ///
        /// scala.util.hashing =>
        /// https://github.com/scala/scala/blob/2.12.x/src/library/scala/util/hashing/MurmurHash3.scala
        /// C port of MurmurHash3 =>
        /// https://github.com/PeterScott/murmur3/blob/master/murmur3.c
        /// </summary>
        private static class Murmur3
        {
            /// <summary>
            /// Force all bits of the hash to avalanche. Used for finalizing the hash.
            /// </summary>
            /// <param name="h"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint Avalanche(uint h)
            {
                unchecked
                {
                    h ^= h >> 16;
                    h *= 0x85ebca6b;
                    h ^= h >> 13;
                    h *= 0xc2b2ae35;
                    h ^= h >> 16;
                }
                return h;
            }

            /// <summary>
            /// May optionally be used as the last mixing step. Is a little bit faster than mix,
            /// as it does no further mixing of the resulting hash. For the last element this is not
            /// necessary as the hash is thoroughly mixed during finalization anyway.
            /// </summary>
            /// <param name="h"></param>
            /// <param name="k"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint MixLast(uint h, uint k)
            {
                unchecked
                {
                    k *= 0xcc9e2d51; // c1
                    k = k.RotateLeft(15);
                    k *= 0x1b873593; // c2
                }
                return h ^ k;
            }

            /// <summary>
            /// Mix in a block of data into an intermediate hash value.
            /// </summary>
            /// <param name="hash"></param>
            /// <param name="data"></param>
            /// <returns></returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint Mix(uint hash, uint data)
            {
                var h = MixLast(hash, data);
                h = h.RotateLeft(13);
                unchecked
                {
                    return h * 5 + 0xe6546b64;
                }
            }

            private static uint HashLong(long value, uint seed)
            {
                var val = (ulong)value;
                var h = seed;
                h = Mix(h, (uint)val);
                h = MixLast(h, (uint)(val >> 32));
                return Avalanche(h ^ 2);
            }

            private static uint HashBytes(byte[] data, uint seed)
            {
                using (var stream = new MemoryStream(data))
                using (var reader = new BinaryReader(stream))
                {
                    var blocks = data.Length / 4;
                    var h = seed;
                    uint k;

                    // Body
                    while (blocks > 0)
                    {
                        k = reader.ReadUInt32();
                        h = Mix(h, k);
                        --blocks;
                    }

                    // Tail
                    k = 0;
                    var i = stream.Position;
                    var mod = data.Length & 3;
                    if (mod == 3) k ^= (uint)data[i + 2] << 16;
                    if (mod >= 2) k ^= (uint)data[i + 1] << 8;
                    if (mod >= 1)
                    {
                        k ^= data[i];
                        h = MixLast(h, k);
                    }

                    // Finalization 
                    return Avalanche(h ^ (uint)data.Length);
                }
            }

            private static int Hash(object o, uint seed = 0)
            {
                if (o is null)
                    return 0;

                switch (o)
                {
                    case IActorRef ar:
                        return ar.GetHashCode();
                    case string str:
                        return Hash(Encoding.UTF8.GetBytes(str));
                    case long l:
                        return (int)HashLong(l, seed);
                    case int i:
                        return (int)HashLong(i, seed);
                    case double d:
                        return (int)HashLong(BitConverter.DoubleToInt64Bits(d), seed);
                    case float f:
                        var bytes = BitConverter.GetBytes(f);
                        return (int)HashLong(BitConverter.ToUInt32(bytes, 0), seed);
                    case byte[] bs:
                        return (int)HashBytes(bs, seed);
                    default:
                        return Hash(o.ToString());
                }
            }

            /// <summary>
            /// Hash item using pair independent hash functions.
            ///
            /// Implementation based on "Less Hashing, Same Performance: Building a Better Bloom Filter"
            /// https://www.eecs.harvard.edu/~michaelm/postscripts/tr-02-05.pdf
            /// </summary>
            /// <param name="item">what should be hashed</param>
            /// <param name="hashBuckets">where hashes should be placed</param>
            /// <param name="limit">value to shrink result</param>
            public static void HashBuckets(object item, int[] hashBuckets, int limit)
            {
                var hash1 = Hash(item);
                var hash2 = (int)HashLong(hash1, (uint)hash1);
                var depth = hashBuckets.Length;
                var mask = limit - 1;
                for (var i = 0; i < depth; ++i)
                {
                    // shrink done by AND instead MOD. Assume limit is power of 2
                    hashBuckets[i] = Math.Abs((hash1 + i * hash2) & mask);
                }
            }
        }
    }
}
