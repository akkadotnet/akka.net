//-----------------------------------------------------------------------
// <copyright file="MaterializationBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
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
        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;

        private IRunnableGraph<Task> _simpleGraph = null!;
        private IRunnableGraph<Task<int>> _restartGraph = null!;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("system");
            _materializer = _system.Materializer();

            _simpleGraph = Source.Single(1)
                .Select(x => x + 1)
                .ToMaterialized(Sink.ForEach<int>(_ => { }), Keep.Right);

            _restartGraph = Source.Single(1)
                .Via(RestartFlow.OnFailuresWithBackoff(
                    static () => Flow.Create<int>().Select(static value => value),
                    RestartSettings.Create(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), 0)))
                .ToMaterialized(Sink.First<int>(), Keep.Right);
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            _materializer.Dispose();
            await _system.Terminate();
        }

        [Benchmark]
        public async Task Actor_materializer_run_restart_flow_graph()
        {
            if (await _restartGraph.Run(_materializer) != 1)
                throw new InvalidOperationException("RestartFlow did not deliver its one element.");
        }

        [Benchmark]
        public Task Actor_materializer_run_simple_linear_graph()
        {
            return _simpleGraph.Run(_materializer);
        }
    }
}
