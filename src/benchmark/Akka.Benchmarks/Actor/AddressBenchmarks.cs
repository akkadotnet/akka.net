//-----------------------------------------------------------------------
// <copyright file="AddressBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using static Akka.Benchmarks.Configurations.BenchmarkCategories;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AddressBenchmarks
    {
        private Address _x;
        private Address _y;

        [GlobalSetup]
        public void Setup()
        {
            _x = new Address("akka.tcp", "test-system", "10.62.0.101", 4000);
            _y = new Address("akka.tcp", "test-system", "10.62.0.101", 4123);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public Address Address_Parse()
        {
            return Address.Parse("akka.tcp://test-system@10.62.0.100:5000/");
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public int Address_CompareTo()
        {
            return _x.CompareTo(_y);
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public string Address_ToString()
        {
            return _x.ToString();
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public bool Address_Equals()
        {
            return _x == _y;
        }

        [Benchmark]
        [BenchmarkCategory(MicroBenchmark, AkkaActorBenchmark)]
        public int Address_GetHashCode()
        {
            return _x.GetHashCode();
        }
    }
}
