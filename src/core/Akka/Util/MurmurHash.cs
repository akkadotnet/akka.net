using System;
using System.Collections;
using System.Text;

namespace Akka.Util
{
    /// <summary>
    /// A Murmur3 implementation in .NET that doesn't suck. Imported from https://github.com/markedup-mobi/openmetrics
    /// 
    /// This is a C# port of the cannonical algorithm in C++, with some helper functions
    /// designed to make it easier to work with POCOs and .NET primitives.
    /// </summary>
    public static class Murmur3
    {

        #region Constants

        private const uint ObjectSeed = 0xef91da3;
        private const uint BytesSeed = 0x1a7d9dfe;
        private const uint StringSeed = 0x331df49;

        /* Constants for 32-bit hashing */

        private const uint X86_32_C1 = 0xcc9e2d51;

        private const uint X86_32_C2 = 0x1b873593;

        /* Constants for 64-bit hashing */

        private const ulong X64_128_C1 = 0x87c37b91114253d5L;

        private const ulong X64_128_C2 = 0x4cf5ad432745937fL;

        #endregion

        #region Internal 32-bit hashing helpers

        /// <summary>
        /// Rotate a 32-bit unsigned integer to the left by <see cref="shift"/> bits
        /// </summary>
        /// <param name="original">Original value</param>
        /// <param name="shift">The shift value</param>
        /// <returns>The rotated 32-bit integer</returns>
        private static uint RotateLeft32(uint original, int shift)
        {
            return (original << shift) | (original >> (32 - shift));
        }

        /// <summary>
        /// Rotate a 64-bit unsigned integer to the left by <see cref="shift"/> bits
        /// </summary>
        /// <param name="original">Original value</param>
        /// <param name="shift">The shift value</param>
        /// <returns>The rotated 64-bit integer</returns>
        private static ulong RotateLeft64(ulong original, int shift)
        {
            return (original << shift) | (original >> (64 - shift));
        }



        private static uint Mix32(uint hash, uint data)
        {
            var h1 = MixLast32(hash, data);
            h1 = RotateLeft32(h1, 13);
            h1 = h1 + (5 + 0xe6546b64);
            return h1;
        }

        private static uint MixLast32(uint hash, uint data)
        {
            var k1 = data;

            k1 *= X86_32_C1;
            k1 = RotateLeft32(k1, 15);
            k1 *= X86_32_C2;

            hash ^= k1;
            return hash;
        }

        /// <summary>
        /// Finalization mix - force all bits of a hash block to avalanche.
        /// 
        /// I have no idea what that means but it sound awesome.
        /// </summary>
        private static uint Avalanche32(uint h)
        {
            h ^= h >> 16;
            h *= 0x85ebca6b;
            h ^= h >> 13;
            h *= 0xc2b2ae35;
            h ^= h >> 16;

            return h;
        }

        #endregion

        /// <summary>
        /// Pass a .NET object to the function and get a 32-bit Murmur3 hash in return.
        /// </summary>
        /// <param name="obj">the object to hash.</param>
        /// <returns>A hash value, expressed as a 32 bit integer.</returns>
        public static int Hash(object obj)
        {
            var objbytes = GetObjBytes(obj);
            if (obj == null) return 0; //short-circuit the hash if the value is null
            var hash = Hash_X86_32(objbytes, objbytes.Length, ObjectSeed);
            return hash;
        }

        /// <summary>
        /// Pass a .NET object to the function and get a 32-bit Murmur3 hash in return.
        /// </summary>
        /// <param name="bytes">An array of bytes to hash</param>
        /// <returns>A hash value, expressed as a 32 bit integer.</returns>
        public static int HashBytes(byte[] bytes)
        {
            return Hash_X86_32(bytes, bytes.Length, BytesSeed);
        }

        /// <summary>
        /// Pass a .NET <see cref="string"/> to the function and get a 32-bit Murmur3 hash in return.
        /// </summary>
        /// <param name="str">A string to hash</param>
        /// <returns>A hash value, expressed as a 32 bit integer.</returns>
        public static int HashString(string str)
        {
            var bytes = GetObjBytes(str);
            return Hash_X86_32(bytes, bytes.Length, StringSeed);
        }

        /// <summary>
        /// Translate the offered object into a byte array.
        /// </summary>
        /// <param name="obj">An arbitrary .NET object</param>
        /// <returns>The object encoded into bytes - in the case of custom classes, the hashcode may be used.</returns>
        private static byte[] GetObjBytes(object obj)
        {
            while (true)
            {
                if (obj == null)
                    return new byte[] { 0 };
                if (obj is byte[])
                    return (byte[])obj;
                if (obj is int)
                    return BitConverter.GetBytes((int)obj);
                if (obj is uint)
                    return BitConverter.GetBytes((uint)obj);
                if (obj is short)
                    return BitConverter.GetBytes((short)obj);
                if (obj is ushort)
                    return BitConverter.GetBytes((ushort)obj);
                if (obj is bool)
                    return BitConverter.GetBytes((bool)obj);
                if (obj is long)
                    return BitConverter.GetBytes((long)obj);
                if (obj is ulong)
                    return BitConverter.GetBytes((ulong)obj);
                if (obj is char)
                    return BitConverter.GetBytes((char)obj);
                if (obj is float)
                    return BitConverter.GetBytes((float)obj);
                if (obj is double)
                    return BitConverter.GetBytes((double)obj);
                if (obj is decimal)
                    return new BitArray(decimal.GetBits((decimal)obj)).ToBytes();
                if (obj is Guid)
                    return ((Guid)obj).ToByteArray();
                if (obj is string)
                    return Encoding.Unicode.GetBytes((string)obj);
                obj = obj.ToString();
            }
        }

        /// <summary>
        /// Compute a 32-bit Murmur3 hash for an X86 system.
        /// </summary>
        /// <param name="data">The data that needs to be hashed</param>
        /// <param name="length">The length of the data being hashed</param>
        /// <param name="seed">A seed value used to compute the hash</param>
        /// <returns>A computed hash value, as a signed integer.</returns>
        public static int Hash_X86_32(byte[] data, int length, uint seed)
        {

            var nblocks = length;
            var h1 = seed;
            uint k1 = 0;

            var i = 0;
            while (nblocks >= 4)
            {
                var k = data[i + 0] & 0xFF;
                k |= (data[i + 1] & 0xFF) << 8;
                k |= (data[i + 2] & 0xFF) << 16;
                k |= (data[i + 3] & 0xFF) << 24;

                unchecked
                {
                    h1 = Mix32(h1, (uint)k);
                }

                i += 4;
                nblocks -= 4;
            }

            //tail - there's an unprocessed tail of data that we need to hash
            switch (length)
            {
                case 3:
                    k1 ^= (((uint)data[i + 2] & 0xFF) << 16);
                    goto case 2; //thanks for the code smell, C#!
                case 2:
                    k1 ^= (((uint)data[i + 1] & 0xFF) << 8);
                    goto case 1;
                case 1:
                    k1 ^= ((uint)data[i] & 0xFF);
                    h1 = MixLast32(h1, k1);
                    break;
            }

            //finalization
            h1 ^= (uint)length;
            h1 = Avalanche32(h1);
            unchecked
            {
                return (int)h1;
            }
        }


        #region 64-bit hash functions

        /// <summary>
        /// Compute a 128-bit Murmur3 hash for an X64 system.
        /// </summary>
        /// <param name="data">The data that needs to be hashed</param>
        /// <param name="length">The length of the data being hashed</param>
        /// <param name="seed">A seed value used to compute the hash</param>
        /// <returns>A computed hash value, as an array consisting of two unsigned long integers.</returns>
        public static ulong[] Hash_X64_128(byte[] data, int length, uint seed)
        {
            ulong h1 = seed;
            ulong h2 = seed;
            ulong k1, k2 = 0;
            var nblocks = length >> 4; // /16

            for (var i = 0; i < nblocks; i++)
            {
                k1 = GetBlock64(data, i << 3);
                k2 = GetBlock64(data, (i + 1) << 3);

                k1 *= X64_128_C1;
                k1 = RotateLeft64(k1, 31);
                k1 *= X64_128_C2;
                h1 ^= k1;

                h1 = RotateLeft64(h1, 27);
                h1 += h2;
                h1 += h2; h1 = h1 * 5 + 0x52dce729;

                k2 *= X64_128_C2;
                k2 = RotateLeft64(k2, 33);
                k2 *= X64_128_C1;
                h2 ^= k2;
                h2 = RotateLeft64(h2, 31);
                h2 += h1; h2 = h2 * 5 + 0x38495ab5;
            }

            //tail - there's an unprocessed tail of data that we need to hash
            var offset = (nblocks << 4); // nblocks * 16
            k1 = 0; k2 = 0;
            switch (length & 15)
            {
                case 15: k2 ^= ((ulong)data[offset + 14]) << 48;
                    goto case 14;
                case 14: k2 ^= ((ulong)data[offset + 13]) << 40;
                    goto case 13;
                case 13: k2 ^= ((ulong)data[offset + 12]) << 32;
                    goto case 12;
                case 12: k2 ^= ((ulong)data[offset + 11]) << 24;
                    goto case 11;
                case 11: k2 ^= ((ulong)data[offset + 10]) << 16;
                    goto case 10;
                case 10: k2 ^= ((ulong)data[offset + 9]) << 8;
                    goto case 9;
                case 9: k2 ^= ((ulong)data[offset + 8]) << 0;
                    k2 *= X64_128_C2; k2 = RotateLeft64(k2, 33); k2 *= X64_128_C1; h2 ^= k2;
                    goto case 8;
                case 8: k1 ^= ((ulong)data[offset + 7]) << 56;
                    goto case 7;
                case 7: k1 ^= ((ulong)data[offset + 6]) << 48;
                    goto case 6;
                case 6: k1 ^= ((ulong)data[offset + 5]) << 40;
                    goto case 5;
                case 5: k1 ^= ((ulong)data[offset + 4]) << 32;
                    goto case 4;
                case 4: k1 ^= ((ulong)data[offset + 3]) << 24;
                    goto case 3;
                case 3: k1 ^= ((ulong)data[offset + 2]) << 16;
                    goto case 2;
                case 2: k1 ^= ((ulong)data[offset + 1]) << 8;
                    goto case 1;
                case 1: k1 ^= ((ulong)data[offset + 0]) << 0;
                    k1 *= X64_128_C1; k1 = RotateLeft64(k1, 31); k1 *= X64_128_C2; h1 ^= k1;
                    break;
            }

            //finalization
            h1 ^= (ulong)length; h2 ^= (ulong)length;

            h1 += h2;
            h2 += h1;

            h1 = ForceMix64(h1);
            h2 = ForceMix64(h2);

            h1 += h2;
            h2 += h1;

            return new[] { h1, h2 };
        }



        /// <summary>
        /// Read the next 8-byte block (int64) from a block number
        /// </summary>
        /// <param name="blocks">the original byte array</param>
        /// <param name="i">the current block count</param>
        /// <returns>An unsigned 64-bit integer</returns>
        private static ulong GetBlock64(byte[] blocks, int i)
        {
            unchecked
            {
                return (ulong)BitConverter.ToInt64(blocks, i);
            }
        }

        /// <summary>
        /// Finalization mix - force all bits of a hash block to avalanche.
        /// 
        /// I have no idea what that means but it sound awesome.
        /// </summary>
        private static ulong ForceMix64(ulong k)
        {
            k ^= k >> 33;
            k *= 0xff51afd7ed558ccd;
            k ^= k >> 33;
            k *= 0xc4ceb9fe1a85ec53;
            k ^= k >> 33;

            return k;
        }


        #endregion
    }

    /// <summary>
    /// Extension method class to make it easier to work with <see cref="BitArray"/> instances
    /// </summary>
    public static class BitArrayHelpers
    {
        /// <summary>
        /// Converts a <see cref="BitArray"/> into an array of <see cref="byte"/>
        /// </summary>
        public static byte[] ToBytes(this BitArray arr)
        {
            if (arr.Count != 8)
            {
                throw new ArgumentException("Not enough bits to make a byte!");
            }
            var bytes = new byte[(arr.Length - 1) / 8 + 1];
            arr.CopyTo(bytes, 0);
            return bytes;
        }
    }
}
