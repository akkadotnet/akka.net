using System;
using System.Collections.Generic;
using System.Text;
using Akka.Benchmarks.Configurations;
using Akka.Remote.Serialization;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class FastHashBenchmarks
    {
        public const string HashKey1 = "hash1";

        [Benchmark]
        public int FastHash_OfString()
        {
            return FastHash.OfString(HashKey1);
        }

        [Benchmark]
        public int FastHash_OfStringUnsafe()
        {
            return FastHash.OfStringFast(HashKey1);
        }
    }
}
