//-----------------------------------------------------------------------
// <copyright file="FastLazyBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Akka.Benchmarks.Configurations;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Utils
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class FastLazyBenchmarks
    {
        private Lazy<int> lazySafe;
        private Lazy<int> lazyUnsafe;
        private FastLazy<int> fastLazy;
        private FastLazy<int, int> fastLazyWithInit;


        [GlobalSetup]
        public void Setup()
        {
            lazySafe = new Lazy<int>(() => 100, LazyThreadSafetyMode.ExecutionAndPublication);
            lazyUnsafe = new Lazy<int>(() => 100, LazyThreadSafetyMode.None);
            fastLazy = new FastLazy<int>(() => 100);
            fastLazyWithInit = new FastLazy<int, int>(state => state + 100, 1000);
        }

        [Benchmark(Baseline = true)]
        public int Lazy_safe_get_value()
        {
            return lazySafe.Value;
        }

        [Benchmark]
        public int Lazy_unsafe_get_value()
        {
            return lazyUnsafe.Value;
        }

        [Benchmark]
        public int FastLazy_get_value()
        {
            return fastLazy.Value;
        }

        [Benchmark]
        public int FastLazy_satefull_get_value()
        {
            return fastLazyWithInit.Value;
        }
    }
}
