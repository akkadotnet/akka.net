//-----------------------------------------------------------------------
// <copyright file="AddressBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private Address _x;
        private Address _y;
        private Address _z;

        [IterationSetup]
        public void Setup()
        {
            _x = new Address("akka.tcp", "test-system", "10.62.0.101", 4000);
            _y = new Address("akka.tcp", "test-system", "10.62.0.101", 4123);
            
            // same as X, but not literally the same instance so we can't short-circuit the `==` comparison
            _z = new Address("akka.tcp", "test-system", "10.62.0.101", 4000);
        }

        [Benchmark]
        public Address Address_Parse()
        {
            return Address.Parse("akka.tcp://test-system@10.62.0.100:5000/");
        }

        [Benchmark]
        public int Address_CompareTo()
        {
            return _x.CompareTo(_y);
        }

        [Benchmark]
        public string Address_ToString()
        {
            return _x.ToString();
        }

        [Benchmark]
        public bool Address_Equals_Different()
        {
            return _x == _y;
        }
        
        [Benchmark]
        public bool Address_Equals_Same()
        {
            return _x == _z;
        }

        [Benchmark]
        public int Address_GetHashCode()
        {
            return _x.GetHashCode();
        }
    }
}
