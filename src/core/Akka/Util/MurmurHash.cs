//-----------------------------------------------------------------------
// <copyright file="MurmurHash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Util
{
    /// <summary>
    /// Murmur3 Hash implementation
    /// </summary>
    public static class MurmurHash
    {
        // Magic values used for MurmurHash's 32 bit hash.
        // Don't change these without consulting a hashing expert!
        private const uint VisibleMagic = 0x971e137b;
        private const uint HiddenMagicA = 0x95543787;
        private const uint HiddenMagicB = 0x2ad7eb25;
        private const uint VisibleMixer = 0x52dce729;
        private const uint HiddenMixerA = 0x7b7d159c;
        private const uint HiddenMixerB = 0x6bce6396;
        private const uint FinalMixer1 = 0x85ebca6b;
        private const uint FinalMixer2 = 0xc2b2ae35;

        // Arbitrary values used for hashing certain classes

        private const uint StringSeed = 0x331df49;
        private const uint ArraySeed = 0x3c074a61;

        /** The first 23 magic integers from the first stream are stored here */
        private static readonly uint[] StoredMagicA;

        /** The first 23 magic integers from the second stream are stored here */
        private static readonly uint[] StoredMagicB;

        /// <summary>
        /// The initial magic integer in the first stream.
        /// </summary>
        public const uint StartMagicA = HiddenMagicA;

        /// <summary>
        /// The initial magic integer in the second stream.
        /// </summary>
        public const uint StartMagicB = HiddenMagicB;

        /// <summary>
        /// TBD
        /// </summary>
        static MurmurHash()
        {
            //compute range of values for StoredMagicA
            var storedMagicA = new List<uint>();
            var nextMagicA = HiddenMagicA;
            foreach (var i in Enumerable.Repeat(0, 23))
            {
                nextMagicA = NextMagicA(nextMagicA);
                storedMagicA.Add(nextMagicA);
            }
            StoredMagicA = storedMagicA.ToArray();

            //compute range of values for StoredMagicB
            var storedMagicB = new List<uint>();
            var nextMagicB = HiddenMagicB;
            foreach (var i in Enumerable.Repeat(0, 23))
            {
                nextMagicB = NextMagicB(nextMagicB);
                storedMagicB.Add(nextMagicB);
            }
            StoredMagicB = storedMagicB.ToArray();
        }

        /// <summary>
        /// Begin a new hash with a seed value.
        /// </summary>
        /// <param name="seed">TBD</param>
        /// <returns>TBD</returns>
        public static uint StartHash(uint seed)
        {
            return seed ^ VisibleMagic;
        }

        /// <summary>
        /// Given a magic integer from the first stream, compute the next
        /// </summary>
        /// <param name="magicA">TBD</param>
        /// <returns>TBD</returns>
        public static uint NextMagicA(uint magicA)
        {
            return magicA * 5 + HiddenMixerA;
        }

        /// <summary>
        /// Given a magic integer from the second stream, compute the next
        /// </summary>
        /// <param name="magicB">TBD</param>
        /// <returns>TBD</returns>
        public static uint NextMagicB(uint magicB)
        {
            return magicB * 5 + HiddenMixerB;
        }

        /// <summary>
        /// Incorporates a new value into an existing hash
        /// </summary>
        /// <param name="hash">The prior hash value</param>
        /// <param name="value">The new value to incorporate</param>
        /// <param name="magicA">A magic integer from the left of the stream</param>
        /// <param name="magicB">A magic integer from a different stream</param>
        /// <returns>The updated hash value</returns>
        public static uint ExtendHash(uint hash, uint value, uint magicA, uint magicB)
        {
            return (hash ^ RotateLeft32(value * magicA, 11) * magicB) * 3 + VisibleMixer;
        }

        /// <summary>
        /// Once all hashes have been incorporated, this performs a final mixing.
        /// </summary>
        /// <param name="hash">TBD</param>
        /// <returns>TBD</returns>
        public static uint FinalizeHash(uint hash)
        {
            var h = (hash ^ (hash >> 16));
            h *= FinalMixer1;
            h ^= h >> 13;
            h *= FinalMixer2;
            h ^= h >> 16;
            return h;
        }

        #region Internal 32-bit hashing helpers

        /// <summary>
        /// Rotate a 32-bit unsigned integer to the left by <paramref name="shift"/> bits
        /// </summary>
        /// <param name="original">Original value</param>
        /// <param name="shift">The shift value</param>
        /// <returns>The rotated 32-bit integer</returns>
        private static uint RotateLeft32(uint original, int shift)
        {
            return (original << shift) | (original >> (32 - shift));
        }

        /// <summary>
        /// Rotate a 64-bit unsigned integer to the left by <paramref name="shift"/> bits
        /// </summary>
        /// <param name="original">Original value</param>
        /// <param name="shift">The shift value</param>
        /// <returns>The rotated 64-bit integer</returns>
        private static ulong RotateLeft64(ulong original, int shift)
        {
            return (original << shift) | (original >> (64 - shift));
        }

        #endregion

        /// <summary>
        /// Compute a high-quality hash of a byte array
        /// </summary>
        /// <param name="b">TBD</param>
        /// <returns>TBD</returns>
        public static int ByteHash(byte[] b)
        {
            return ArrayHash(b);
        }

        /// <summary>
        /// Compute a high-quality hash of an array
        /// </summary>
        /// <param name="a">TBD</param>
        /// <returns>TBD</returns>
        public static int ArrayHash<T>(T[] a)
        {
            unchecked
            {
                var h = StartHash((uint)a.Length * ArraySeed);
                var c = HiddenMagicA;
                var k = HiddenMagicB;
                var j = 0;
                while (j < a.Length)
                {
                    h = ExtendHash(h, (uint)a[j].GetHashCode(), c, k);
                    c = NextMagicA(c);
                    k = NextMagicB(k);
                    j += 1;
                }
                return (int)FinalizeHash(h);
            }
        }

        /// <summary>
        /// Compute high-quality hash of a string
        /// </summary>
        /// <param name="s">TBD</param>
        /// <returns>TBD</returns>
        public static int StringHash(string s)
        {
            unchecked
            {
                var sChar = s.ToCharArray();
                var h = StartHash((uint)s.Length * StringSeed);
                var c = HiddenMagicA;
                var k = HiddenMagicB;
                var j = 0;
                while (j + 1 < s.Length)
                {
                    var i = (uint)((sChar[j] << 16) + sChar[j + 1]);
                    h = ExtendHash(h, i, c, k);
                    c = NextMagicA(c);
                    k = NextMagicB(k);
                    j += 2;
                }
                if (j < s.Length) h = ExtendHash(h, sChar[j], c, k);
                return (int)FinalizeHash(h);
            }
        }

        /// <summary>
        /// Compute a hash that is symmetric in its arguments--that is,
        /// where the order of appearance of elements does not matter.
        /// This is useful for hashing sets, for example.
        /// </summary>
        /// <param name="xs">TBD</param>
        /// <param name="seed">TBD</param>
        /// <returns>TBD</returns>
        public static int SymmetricHash<T>(IEnumerable<T> xs, uint seed)
        {
            unchecked
            {
                uint a = 0, b = 0, n = 0;
                uint c = 1;
                foreach (var i in xs)
                {
                    var u = (uint)i.GetHashCode();
                    a += u;
                    b ^= u;
                    if (u != 0) c *= u;
                    n += 1;
                }

                var h = StartHash(seed*n);
                h = ExtendHash(h, a, StoredMagicA[0], StoredMagicB[0]);
                h = ExtendHash(h, b, StoredMagicA[1], StoredMagicB[1]);
                h = ExtendHash(h, c, StoredMagicA[2], StoredMagicB[2]);
                return (int)FinalizeHash(h);
            }
        }
    } 

    /// <summary>
    /// Extension method class to make it easier to work with <see cref="BitArray"/> instances
    /// </summary>
    public static class BitArrayHelpers
    {
        /// <summary>
        /// Converts a <see cref="BitArray"/> into an array of <see cref="byte"/>
        /// </summary>
        /// <param name="arr">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if there aren't enough bits in the given <paramref name="arr"/> to make a byte.
        /// </exception>
        /// <returns>TBD</returns>
        public static byte[] ToBytes(this BitArray arr)
        {
            if (arr.Length != 8)
            {
                throw new ArgumentException("Not enough bits to make a byte!", nameof(arr));
            }
            var bytes = new byte[(arr.Length - 1) / 8 + 1];
            ((ICollection)arr).CopyTo(bytes, 0);
            return bytes;
        }
    }
}

