//-----------------------------------------------------------------------
// <copyright file="AddressBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AddressBenchmarks
    {
        private Address x;
        private Address y;

        [GlobalSetup]
        public void Setup()
        {
            x = new Address("akka.tcp", "test-system", "10.62.0.101", 4000);
            y = new Address("akka.tcp", "test-system", "10.62.0.101", 4123);
        }

        [Benchmark]
        public Address Address_Parse()
        {
            return Address.Parse("akka.tcp://test-system@10.62.0.100:5000/");
        }

        [Benchmark]
        public int Address_CompareTo()
        {
            return x.CompareTo(y);
        }

        [Benchmark]
        public string Address_ToString()
        {
            return x.ToString();
        }

        [Benchmark]
        public bool Address_Equals()
        {
            return x == y;
        }

        [Benchmark]
        public int Address_GetHashCode()
        {
            return x.GetHashCode();
        }
    }
}
