//-----------------------------------------------------------------------
// <copyright file="MergeManyBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using NBench;

namespace Akka.Streams.Tests.Performance
{
    // JVM : FlatMapMergeBenchmark
    public class MergeManyBenchmark
    {
        private ActorSystem _actorSystem;
        private ActorMaterializerSettings _materializerSettings;
        private ActorMaterializer _materializer;
        private IRunnableGraph<Task> _takeGraph;
        private IRunnableGraph<Task> _singleGraph;
        private IRunnableGraph<Task> _tenGraph;

        private const int NumberOfElements = 100000;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _actorSystem = ActorSystem.Create("MergeManyBenchmark",
                ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf"));
            _actorSystem.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            _materializerSettings = ActorMaterializerSettings.Create(_actorSystem).WithDispatcher("akka.test.stream-dispatcher");
            _materializer = _actorSystem.Materializer(_materializerSettings);

            var takeSource = CreateSource(NumberOfElements);

            var singleSubSource = CreateSource(NumberOfElements);
            var singleSource = Source.Repeat(0).Take(1).MergeMany(1, _ => singleSubSource);

            var tenSubSources = CreateSource(NumberOfElements/10);
            var tenSources = Source.Repeat(0).Take(10).MergeMany(10, _ => tenSubSources);

            _takeGraph = ToSource(takeSource);
            _singleGraph = ToSource(singleSource);
            _tenGraph = ToSource(tenSources);
        }

        private static IRunnableGraph<Task> ToSource(IGraph<SourceShape<int>, NotUsed> graph)
            => Source.FromGraph(graph).ToMaterialized(Sink.Ignore<int>(), Keep.Right);

        private static IGraph<SourceShape<int>, NotUsed> CreateSource(int count)
            => Fusing.Aggressive(Source.Repeat(1).Take(count));

        [PerfCleanup]
        public void Shutdown() => _actorSystem.Terminate().Wait(TimeSpan.FromSeconds(5));


        [PerfBenchmark(Description = "Test the performance of a single source without using MergeMany",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 200)]
        public void Take_100k_elements() => _takeGraph.Run(_materializer).Wait();


        [PerfBenchmark(Description = "Test the performance of a single source using MergeMany",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 500)]
        public void Select_many_from_1_Source_100k_elements() => _singleGraph.Run(_materializer).Wait();


        [PerfBenchmark(Description = "Test the performance from 10 sources using MergeMany",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 600)]
        public void Select_many_from_10_Sources_10k_elements_each() => _tenGraph.Run(_materializer).Wait();
    }
}
