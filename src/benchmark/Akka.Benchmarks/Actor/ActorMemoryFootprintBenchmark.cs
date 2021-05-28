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

namespace Akka.Benchmarks.Actor
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class ActorMemoryFootprintBenchmark
    {
        public ActorSystem Sys;
        public Props Props;

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
            Sys.ActorOf(Props);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            await Sys.Terminate();
        }
    }
}
