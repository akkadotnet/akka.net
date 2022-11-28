// //-----------------------------------------------------------------------
// <copyright file="SpanHackBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Benchmarks.Configurations;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Utils
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class SpanHackBenchmarks
    {
        [Params(1, 1000, long.MaxValue)]
        public long Formatted { get; set; }
        
        [Benchmark]
        public int Int64CharCountBenchmark()
        {
            return SpanHacks.Int64SizeInCharacters(Formatted);
        }
    }
}