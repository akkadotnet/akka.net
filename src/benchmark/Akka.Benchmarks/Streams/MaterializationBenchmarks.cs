// //-----------------------------------------------------------------------
// // <copyright file="MaterializationBenchmarks.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

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

        private IRunnableGraph<NotUsed> flowWithMap;
        private IRunnableGraph<NotUsed> graphWithJunctionsGradual;
        private IRunnableGraph<NotUsed> graphWithJunctionsImmediate;
        private IRunnableGraph<NotUsed> graphWithImportedFlow;
        private IRunnableGraph<Task> subStream;

        private const int SubstreamCount = 10_000;

        [Params(1, 10, 100)]
        public int Complexity;

        [GlobalSetup]
        public void Setup()
        {
            system = ActorSystem.Create("system");
            materializer = system.Materializer();

            flowWithMap = FlowWithMapBuilder(Complexity);
            graphWithJunctionsGradual = GraphWithJunctionsGradualBuilder(Complexity);
            graphWithJunctionsImmediate = GraphWithJunctionsImmediateBuilder(Complexity);
            graphWithImportedFlow = GraphWithImportedFlowBuilder(Complexity);
            subStream = SubStreamBuilder(Complexity);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            materializer.Dispose();
            system.Dispose();
        }

        [Benchmark]
        public void FlowWithMap() => flowWithMap.Run(materializer);

        [Benchmark]
        public void GraphWithJunctionsGradual() => graphWithJunctionsGradual.Run(materializer);

        [Benchmark]
        public void GraphWithJunctionsImmediate() => graphWithJunctionsImmediate.Run(materializer);

        [Benchmark]
        public void GraphWithImportedFlow() => graphWithImportedFlow.Run(materializer);

        [Benchmark]
        public void SubStream() => subStream.Run(materializer);

        private IRunnableGraph<NotUsed> FlowWithMapBuilder(int operatorsCount)
        {
            var source = Source.Single(1);
            for (int i = 0; i < operatorsCount; i++)
            {
                source = source.Select(x => x);
            }

            return source.To(Sink.Ignore<int>());
        }

        private IRunnableGraph<NotUsed> GraphWithJunctionsGradualBuilder(int junctionCount)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var bcast = b.Add(new Broadcast<int>(junctionCount));
                var outlet = bcast.Out(0);

                for (int i = 1; i < junctionCount; i++)
                {
                    var merge = b.Add(new Merge<int>(2));
                    b.From(outlet).To(merge.In(0));
                    b.From(bcast.Out(i)).To(merge.In(1));
                    outlet = merge.Out;
                }

                b.From(Source.Single(1)).To(bcast);
                b.From(outlet).To(Sink.Ignore<int>());
                return ClosedShape.Instance;
            }));
        }

        private IRunnableGraph<NotUsed> GraphWithJunctionsImmediateBuilder(int junctionCount)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var bcast = b.Add(new Broadcast<int>(junctionCount));
                var merge = b.Add(new Merge<int>(junctionCount));

                for (int i = 0; i < junctionCount; i++)
                {
                    b.From(bcast.Out(i)).To(merge.In(i));
                }

                b.From(Source.Single(1)).To(bcast);
                b.From(merge).To(Sink.Ignore<int>());
                return ClosedShape.Instance;
            }));
        }

        private IRunnableGraph<NotUsed> GraphWithImportedFlowBuilder(int flowCount)
        {
            return RunnableGraph.FromGraph(GraphDsl.Create(Source.Single(1), (b, source) =>
            {
                var flow = Flow.Create<int>().Select(x => x);
                var outlet = source.Outlet;

                for (int i = 0; i < flowCount; i++)
                {
                    var flowShape = b.Add(flow);
                    b.From(outlet).To(flowShape);
                    outlet = flowShape.Outlet;
                }

                b.From(outlet).To(Sink.Ignore<int>());
                return ClosedShape.Instance;
            }));
        }

        private IRunnableGraph<Task> SubStreamBuilder(int operatorCount)
        {
            Flow<int, int, NotUsed> CreateSubFlow()
            {
                var flow = Flow.Create<int>();
                for (int i = 0; i < operatorCount; i++)
                {
                    flow = flow.Select(x => x); 
                }

                return flow;
            }

            return Source.Repeat(Source.Single(1))
                .Take(SubstreamCount)
                .ConcatMany(source => source.Via(CreateSubFlow()))
                .ToMaterialized(Sink.Last<int>(), Keep.Right);
        }
    }
}