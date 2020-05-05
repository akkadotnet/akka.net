//-----------------------------------------------------------------------
// <copyright file="MaterializationBenchmark.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using NBench;

namespace Akka.Streams.Tests.Performance
{
    public class MaterializationBenchmark
    {
        private ActorSystem _actorSystem;
        private ActorMaterializerSettings _materializerSettings;
        private ActorMaterializer _materializer;

        [PerfSetup]
        public void Setup(BenchmarkContext context)
        {
            _actorSystem = ActorSystem.Create("MaterializationBenchmark",
                ConfigurationFactory.FromResource<ScriptedTest>("Akka.Streams.TestKit.Tests.reference.conf"));
            _actorSystem.Settings.InjectTopLevelFallback(ActorMaterializer.DefaultConfig());
            _materializerSettings =
                ActorMaterializerSettings.Create(_actorSystem).WithDispatcher("akka.test.stream-dispatcher");
            _materializer = _actorSystem.Materializer(_materializerSettings);
        }

        [PerfCleanup]
        public void Shutdown() => _actorSystem.Terminate().Wait(TimeSpan.FromSeconds(5));

        [PerfBenchmark(Description = "Test the performance of the materialization phase for a flow with 1 map stage",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Flow_with_1_map() => FlowWithMapBuilder(1).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a flow with 10 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Flow_with_10_map() => FlowWithMapBuilder(10).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a flow with 100 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 10)]
        public void Flow_with_100_map() => FlowWithMapBuilder(100).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a flow with 1000 map stages",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 150)]
        public void Flow_with_1000_map() => FlowWithMapBuilder(1000).Run(_materializer);



        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1 junction",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_junction() => GraphWithJunctionsBuilder(1).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 10 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_10_junction() => GraphWithJunctionsBuilder(10).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 100 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 20)]
        public void Graph_with_100_junction() => GraphWithJunctionsBuilder(100).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1000 junctions",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1000)]
        public void Graph_with_1000_junction() => GraphWithJunctionsBuilder(1000).Run(_materializer);



        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1 nested import",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_nested_imports() => GraphWithNestedImportsBuilder(1).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 10 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_10_nested_imports() => GraphWithNestedImportsBuilder(10).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 100 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_100_nested_imports() => GraphWithNestedImportsBuilder(100).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1000 nested imports",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3,
            Skip = "Throws StackOverflowException by around 200 nested imports")]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 1000)]
        public void Graph_with_1000_nested_imports() => GraphWithNestedImportsBuilder(1000).Run(_materializer);



        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1 imported flow",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 2)]
        public void Graph_with_1_imported_flow() => GraphWithImportedFlowBuilder(1).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 10 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 5)]
        public void Graph_with_10_imported_flows() => GraphWithImportedFlowBuilder(10).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 100 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Test, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 10)]
        public void Graph_with_100_imported_flows() => GraphWithImportedFlowBuilder(100).Run(_materializer);


        [PerfBenchmark(Description = "Test the performance of the materialization phase for a graph with 1000 imported flows",
            RunMode = RunMode.Iterations, TestMode = TestMode.Measurement, NumberOfIterations = 3)]
        [TimingMeasurement]
        [ElapsedTimeAssertion(MaxTimeMilliseconds = 100)]
        public void Graph_with_1000_imported_flows() => GraphWithImportedFlowBuilder(1000).Run(_materializer);



        #region Builder

        public static IRunnableGraph<NotUsed> FlowWithMapBuilder(int numberOfCombinators)
        {
            var source = Source.Single(1);
            for (var i = 0; i < numberOfCombinators; i++)
                source = source.Select(x => x);
            return source.To(Sink.Ignore<int>());
        }

        public static IRunnableGraph<NotUsed> GraphWithJunctionsBuilder(int numberOfJunctions)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var broadcast = b.Add(new Broadcast<NotUsed>(numberOfJunctions));
                var outlet = broadcast.Out(0);
                for (var i = 1; i < numberOfJunctions; i++)
                {
                    var merge = b.Add(new Merge<NotUsed>(2));
                    b.From(outlet).To(merge);
                    b.From(broadcast.Out(i)).To(merge);
                    outlet = merge.Out;
                }
                b.From(Source.Single(NotUsed.Instance)).To(broadcast);
                b.From(outlet).To(Sink.Ignore<NotUsed>().MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            }));
        }

        public static IRunnableGraph<NotUsed> GraphWithNestedImportsBuilder(int numberOfNestedGraphs)
        {
            var flow = Flow.Create<NotUsed>().Select(x => x);
            for (var i = 0; i < numberOfNestedGraphs; i++)
                flow = Flow.FromGraph(GraphDsl.Create(flow, (b, f) => new FlowShape<NotUsed, NotUsed>(f.Inlet, f.Outlet)));

            return RunnableGraph.FromGraph(GraphDsl.Create(flow, (b, f) =>
            {
                b.From(Source.Single(NotUsed.Instance))
                    .Via(f)
                    .To(Sink.Ignore<NotUsed>().MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            }));
        }

        public static IRunnableGraph<NotUsed> GraphWithImportedFlowBuilder(int numberOfFlows)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(Source.Single(NotUsed.Instance), (b, s) =>
            {
                var flow = Flow.Create<NotUsed>().Select(x => x);
                var outlet = s.Outlet;
                for (var i = 0; i < numberOfFlows; i++)
                {
                    var flowShape = b.Add(flow);
                    b.From(outlet).To(flowShape);
                    outlet = flowShape.Outlet;
                }
                b.From(outlet).To(Sink.Ignore<NotUsed>().MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            }));
        }

        #endregion
    }
}
