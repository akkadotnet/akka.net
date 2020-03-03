//-----------------------------------------------------------------------
// <copyright file="TypeExtensionsBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Benchmarks.Configurations;
using Akka.Streams.Actors;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Utils
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class TypeExtensionsBenchmarks
    {
        private static readonly Type type = typeof(Subscribe<int>);

        [Benchmark(Baseline = true)]
        public string Type_FullName()
        {
            return type.FullName;
        }

        [Benchmark]
        public string Type_QualifiedName()
        {
            return type.TypeQualifiedName();
        }
    }
}
