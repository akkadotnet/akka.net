//-----------------------------------------------------------------------
// <copyright file="ActorMemoryFootprintBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    [SimpleJob(RunStrategy.Monitoring, targetCount: 25, warmupCount: 5)]
    public class ActorMemoryFootprintBenchmark
    {
        public ActorSystem Sys;
        public Props Props;

        [Params(10_000)]
        public int SpawnCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
           Sys = ActorSystem.Create("Bench");
           Props = Props.Create(() => new TempActor());
        }

        private class TempActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                
            }
        }

        [Benchmark]
        public void SpawnActor()
        {
            for(var i = 0; i < SpawnCount; i++)
                Sys.ActorOf(Props);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await Sys.Terminate();
        }
    }
}
