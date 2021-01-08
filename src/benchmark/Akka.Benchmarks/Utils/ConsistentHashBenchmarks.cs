//-----------------------------------------------------------------------
// <copyright file="ConsistentHashBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Text;
using Akka.Benchmarks.Configurations;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Utils
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ConsistentHashBenchmarks
    {

        private string testString;
        private byte[] testBinary;

        [GlobalSetup]
        public void Setup()
        {
            testString = Guid.NewGuid().ToString("D");
            testBinary = new byte[100];

            ThreadLocalRandom.Current.NextBytes(testBinary);
        }

        [Benchmark]
        public int Murmur_string_hash()
        {
            return MurmurHash.StringHash(testString);
        }

        [Benchmark]
        public uint Jenkins_string_hash()
        {
            return Jenkins.Hash(testString);
        }

        [Benchmark]
        public int Murmur_binary_hash()
        {
            return MurmurHash.ByteHash(testBinary);
        }

        [Benchmark]
        public uint Jenkins_binary_hash()
        {
            return Jenkins.Hash(testBinary);
        }

        #region Jenkins

        #region copyright
        // dotnet/orleans is licensed under the MIT License
        //
        // A short and simple permissive license with conditions only requiring
        // preservation of copyright and license notices. Licensed works,
        // modifications, and larger works may be distributed under different
        // terms and without source code.
        #endregion

        public static class Jenkins
        {
            private static void Mix(ref uint a, ref uint b, ref uint c)
            {
                a -= b; a -= c; a ^= (c >> 13);
                b -= c; b -= a; b ^= (a << 8);
                c -= a; c -= b; c ^= (b >> 13);
                a -= b; a -= c; a ^= (c >> 12);
                b -= c; b -= a; b ^= (a << 16);
                c -= a; c -= b; c ^= (b >> 5);
                a -= b; a -= c; a ^= (c >> 3);
                b -= c; b -= a; b ^= (a << 10);
                c -= a; c -= b; c ^= (b >> 15);
            }

            // This is the reference implementation of the Jenkins hash.
            public static uint Hash(byte[] data)
            {
                int len = data.Length;
                uint a = 0x9e3779b9;
                uint b = a;
                uint c = 0;
                int i = 0;

                while (i + 12 <= len)
                {
                    a += (uint)data[i++] |
                        ((uint)data[i++] << 8) |
                        ((uint)data[i++] << 16) |
                        ((uint)data[i++] << 24);
                    b += (uint)data[i++] |
                        ((uint)data[i++] << 8) |
                        ((uint)data[i++] << 16) |
                        ((uint)data[i++] << 24);
                    c += (uint)data[i++] |
                        ((uint)data[i++] << 8) |
                        ((uint)data[i++] << 16) |
                        ((uint)data[i++] << 24);
                    Mix(ref a, ref b, ref c);
                }
                c += (uint)len;
                if (i < len)
                    a += data[i++];
                if (i < len)
                    a += (uint)data[i++] << 8;
                if (i < len)
                    a += (uint)data[i++] << 16;
                if (i < len)
                    a += (uint)data[i++] << 24;
                if (i < len)
                    b += (uint)data[i++];
                if (i < len)
                    b += (uint)data[i++] << 8;
                if (i < len)
                    b += (uint)data[i++] << 16;
                if (i < len)
                    b += (uint)data[i++] << 24;
                if (i < len)
                    c += (uint)data[i++] << 8;
                if (i < len)
                    c += (uint)data[i++] << 16;
                if (i < len)
                    c += (uint)data[i++] << 24;
                Mix(ref a, ref b, ref c);
                return c;
            }

            public static uint Hash(string data)
            {
                byte[] bytesToHash = Encoding.UTF8.GetBytes(data);
                return Hash(bytesToHash);
            }

            // This implementation calculates the exact same hash value as the above, but is
            // optimized for the case where the input is exactly 24 bytes of data provided as
            // three 8-byte unsigned integers.
            public static uint Hash(ulong u1, ulong u2, ulong u3)
            {
                uint a = 0x9e3779b9;
                uint b = a;
                uint c = 0;

                unchecked
                {
                    a += (uint)u1;
                    b += (uint)((u1 ^ (uint)u1) >> 32);
                    c += (uint)u2;
                    Mix(ref a, ref b, ref c);
                    a += (uint)((u2 ^ (uint)u2) >> 32);
                    b += (uint)u3;
                    c += (uint)((u3 ^ (uint)u3) >> 32);
                }
                Mix(ref a, ref b, ref c);
                c += 24;
                Mix(ref a, ref b, ref c);
                return c;
            }
        }

        #endregion
    }
}
