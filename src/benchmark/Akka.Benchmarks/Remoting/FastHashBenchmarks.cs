//-----------------------------------------------------------------------
// <copyright file="FastHashBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
