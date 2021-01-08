//-----------------------------------------------------------------------
// <copyright file="ByteStringBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Benchmarks.Configurations;
using Akka.IO;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ByteStringBenchmarks
    {
        [Params(10, 100, 1000)]
        public int PayloadSize;
        
        private byte[] _bytes;
        private string _str;

        private ByteString _multipart;

        [GlobalSetup]
        public void Setup()
        {
            _bytes = new byte[PayloadSize];
            _str = new string('x', PayloadSize);

            _multipart = ByteString.Empty;
            byte acc = 1;
            for (int i = 0; i < 10; i++)
            {
                var array = new byte[i];
                for (int j = 0; j < i; j++)
                {
                    array[j] = (byte)(acc++ % byte.MaxValue);
                }
                _multipart += ByteString.FromBytes(array);
            }
        }

        [Benchmark]
        public ByteString ByteString_create_unsafe()
        {
            return ByteString.FromBytes(_bytes);
        }

        [Benchmark]
        public ByteString ByteString_create_copying()
        {
            return ByteString.CopyFrom(_bytes);
        }
        
        [Benchmark]
        public ByteString ByteString_create_from_string()
        {
            return ByteString.FromString(_str);
        }

        [Benchmark]
        [Arguments(10)]
        public ByteString ByteString_concatenation(int times)
        {
            var acc = ByteString.Empty;
            for (int i = 0; i < times; i++)
            {
                acc += ByteString.FromBytes(_bytes);
            }

            return acc;
        }

        [Benchmark]
        public ByteString ByteString_multipart_slice()
        {
            return _multipart.Slice(10, 40);
        }

        [Benchmark]
        public ByteString ByteString_multipart_compact()
        {
            return _multipart.Compact();
        }


        [Benchmark]
        public bool ByteString_multipart_has_substring()
        {
            byte[] array = { 6, 7, 8, 9 };
            return _multipart.HasSubstring(ByteString.FromBytes(array), 0);
        }
    }
}
