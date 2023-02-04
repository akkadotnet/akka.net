// //-----------------------------------------------------------------------
// // <copyright file="AtomicWriteBenchmark.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Benchmarks.Configurations;
using Akka.Persistence;
using BenchmarkDotNet.Attributes;

namespace Akka.Cluster.Benchmarks.Persistence
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class AtomicWriteBenchmark
    {
        private IImmutableList<IPersistentRepresentation> _immutableList;
        
        [Params(100000)] public int WriteMsgCount;

        [IterationSetup]
        public void Setup()
        {
            _immutableList = ImmutableList.Create<IPersistentRepresentation>(new Persistent(new object()));
        }

        [Benchmark]
        public void Constructor()
        {
            for (var i = 0; i < WriteMsgCount; i++)
            {
                _ = new AtomicWrite(_immutableList);
            }
        }
    }    
}

