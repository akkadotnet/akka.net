//-----------------------------------------------------------------------
// <copyright file="MaterializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    [Config(typeof(MicroBenchmarkConfig))]
    public class MaterializationBenchmarks
    {
        private ActorSystem system;
        private ActorMaterializer materializer;

        private IRunnableGraph<Task> simpleGraph;

        [GlobalSetup]
        public void Setup()
        {
            system = ActorSystem.Create("system");
            materializer = system.Materializer();

            simpleGraph = Source.Single(1)
                .Select(x => x + 1)
                .ToMaterialized(Sink.ForEach<int>(i => { }), Keep.Right);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            materializer.Dispose();
            system.Dispose();
        }

        [Benchmark]
        public void Actor_materializer_run_simple_linear_graph()
        {
            simpleGraph.Run(materializer);
        }
    }
}
