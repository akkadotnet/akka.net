//-----------------------------------------------------------------------
// <copyright file="SpanHackBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Benchmarks.Configurations;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Utils
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class SpanHackBenchmarks
    {
        [Params(0, 1, -1, 1000, int.MaxValue, long.MaxValue)]
        public long Formatted { get; set; }

        [Benchmark]
        public int Int64CharCountBenchmark()
        {
            return SpanHacks.Int64SizeInCharacters(Formatted);
        }

        [Benchmark]
        public int TryFormatBenchmark()
        {
            Span<char> buffer = stackalloc char[22];
            return SpanHacks.TryFormat(Formatted, 0, ref buffer);
        }
    }
}
