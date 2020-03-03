//-----------------------------------------------------------------------
// <copyright file="GraphOpsIntegrationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphOpsIntegrationSpec : AkkaSpec
    {
        private static class Shuffle
        {
            internal class ShufflePorts: Shape
            {
                private readonly Inlet _in1;
                private readonly Inlet _in2;
                private readonly Outlet _out1;
                private readonly Outlet _out2;

                public ShufflePorts(Inlet in1, Inlet in2, Outlet out1, Outlet out2)
                {
                    _in1 = in1;
                    _in2 = in2;
                    _out1 = out1;
                    _out2 = out2;
                    Inlets = ImmutableArray.Create(in1, in2);
                    Outlets = ImmutableArray.Create(out1, out2);
                }

                public override ImmutableArray<Inlet> Inlets { get; }
                public override ImmutableArray<Outlet> Outlets { get; }

                public Inlet<int> In1 => (Inlet<int>) _in1;

                public Inlet<int> In2 => (Inlet<int>) _in2;

                public Outlet<int> Out1 => (Outlet<int>) _out1;

                public Outlet<int> Out2 => (Outlet<int>) _out2;

                public override Shape DeepCopy()
                    => new ShufflePorts(_in1.CarbonCopy(), _in2.CarbonCopy(), _out1.CarbonCopy(), _out2.CarbonCopy());

                public override Shape CopyFromPorts(ImmutableArray<Inlet> inlets, ImmutableArray<Outlet> outlets)
                {
                    if (Inlets.Length != inlets.Length)
                        throw new ArgumentException("Inlets must have the same size", nameof(inlets));
                    if (Outlets.Length != outlets.Length)
                        throw new ArgumentException("Outlets must have the same size", nameof(outlets));

                    return new ShufflePorts(inlets[0], inlets[1], outlets[0], outlets[1]);
                }
            }

            internal static IGraph<ShufflePorts, NotUsed> Create<TIn, TOut>(Flow<TIn, TOut, NotUsed> pipeline)
            {
                return GraphDsl.Create(b =>
                {
                    var merge = b.Add(new Merge<TIn>(2));
                    var balance = b.Add(new Balance<TOut>(2));

                    b.From(merge.Out).Via(pipeline).To(balance.In);

                    return new ShufflePorts(merge.In(0), merge.In(1), balance.Out(0), balance.Out(1));
                });
            }
        }

        private ActorMaterializer Materializer { get; }

        public GraphOpsIntegrationSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void GraphDSLs_must_support_broadcast_merge_layouts()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var broadcast = b.Add(new Broadcast<int>(2));
                var merge = b.Add(new Merge<int>(2));
                var source = Source.From(Enumerable.Range(1, 3)).MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);

                b.From(source).To(broadcast.In);
                b.From(broadcast.Out(0)).To(merge.In(0));
                b.From(broadcast.Out(1)).Via(Flow.Create<int>().Select(x => x + 3)).To(merge.In(1));
                b.From(merge.Out).Via(Flow.Create<int>().Grouped(10)).To(sink.Inlet);

                return ClosedShape.Instance;
            })).Run(Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 6));
        }

        [Fact]
        public void GraphDSLs_must_support_balance_merge_parallelization_layouts()
        {
            var elements = Enumerable.Range(0, 11).ToList();
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var balance = b.Add(new Balance<int>(5));
                var merge = b.Add(new Merge<int>(5));
                var source = Source.From(elements).MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);

                b.From(source).To(balance.In);

                for (var i = 0; i < 5; i++)
                    b.From(balance.Out(i)).To(merge.In(i));

                b.From(merge.Out).Via(Flow.Create<int>().Grouped(elements.Count*2)).To(sink.Inlet);

                return ClosedShape.Instance;
            })).Run(Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(elements);
        }

        [Fact]
        public void GraphDSLs_must_support_wikipedia_Topological_sorting_2()
        {
            Func<int, Source<int, (Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>)>> source =
                i =>
                    Source.From(new[] {i})
                          .MapMaterializedValue(_ => default((Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>)));

            // see https://en.wikipedia.org/wiki/Topological_sorting#mediaviewer/File:Directed_acyclic_graph.png
            var seqSink = Sink.First<IEnumerable<int>>();
            var t = RunnableGraph.FromGraph(GraphDsl.Create(seqSink, seqSink, seqSink, ValueTuple.Create, (b, sink2,sink9,sink10) =>
            {
                var b3 = b.Add(new Broadcast<int>(2));
                var b7 = b.Add(new Broadcast<int>(2));
                var b11 = b.Add(new Broadcast<int>(3));
                var m8 = b.Add(new Merge<int>(2));
                var m9 = b.Add(new Merge<int>(2));
                var m10 = b.Add(new Merge<int>(2));
                var m11 = b.Add(new Merge<int>(2));

                var in3 = source(3);
                var in5 = source(5);
                var in7 = source(7);

                // First layer
                b.From(in7).To(b7.In);
                b.From(b7.Out(0)).To(m11.In(0));
                b.From(b7.Out(1)).To(m8.In(0));

                b.From(in5).To(m11.In(1));

                b.From(in3).To(b3.In);
                b.From(b3.Out(0)).To(m8.In(1));
                b.From(b3.Out(1)).To(m10.In(0));

                // Second layer
                b.From(m11.Out).To(b11.In);
                b.From(b11.Out(0)).Via(Flow.Create<int>().Grouped(1000)).To(sink2.Inlet);
                b.From(b11.Out(1)).To(m9.In(0));
                b.From(b11.Out(2)).To(m10.In(1));

                b.From(m8.Out).To(m9.In(1));

                // Third layer
                b.From(m9.Out).Via(Flow.Create<int>().Grouped(1000)).To(sink9.Inlet);
                b.From(m10.Out).Via(Flow.Create<int>().Grouped(1000)).To(sink10.Inlet);

                return ClosedShape.Instance;
            })).Run(Materializer);

            var task = Task.WhenAll(t.Item1, t.Item2, t.Item3);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();

            task.Result[0].ShouldAllBeEquivalentTo(new[] {5, 7});
            task.Result[1].ShouldAllBeEquivalentTo(new[] { 3, 5, 7, 7 });
            task.Result[2].ShouldAllBeEquivalentTo(new[] { 3, 5, 7 });
        }

        [Fact]
        public void GraphDSLs_must_allow_adding_of_flows_to_sources_and_sinks_to_flows()
        {
            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var broadcast = b.Add(new Broadcast<int>(2));
                var merge = b.Add(new Merge<int>(2));
                var source = Source.From(Enumerable.Range(1,3)).MapMaterializedValue<Task<IEnumerable<int>>>(_ => null);

                b.From(source.Select(x => x*2)).To(broadcast.In);
                b.From(broadcast.Out(0)).To(merge.In(0));
                b.From(broadcast.Out(1)).Via(Flow.Create<int>().Select(x => x + 3)).To(merge.In(1));
                b.From(merge.Out).Via(Flow.Create<int>().Grouped(10)).To(sink.Inlet);
                
                return ClosedShape.Instance;
            })).Run(Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {2, 4, 6, 5, 7, 9});
        }

        [Fact]
        public void GraphDSLs_must_be_able_to_run_plain_flow()
        {
            var p = Source.From(Enumerable.Range(1, 3)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var s = this.CreateManualSubscriberProbe<int>();
            var flow = Flow.Create<int>().Select(x => x*2);

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                b.From(Source.FromPublisher(p)).Via(flow).To(Sink.FromSubscriber(s));
                return ClosedShape.Instance;
            })).Run(Materializer);

            var sub = s.ExpectSubscription();
            sub.Request(10);
            s.ExpectNext(1*2, 2*2, 3*2).ExpectComplete();
        }

        [Fact]
        public void GraphDSLs_must_be_possible_to_use_as_lego_bricks()
        {
            Func<int, Source<int, (NotUsed, NotUsed, NotUsed, Task<IEnumerable<int>>)>> source =
                i =>
                    Source.From(Enumerable.Range(i, 3))
                        .MapMaterializedValue(_ => default((NotUsed, NotUsed, NotUsed, Task<IEnumerable<int>>)));
            var shuffler = Shuffle.Create(Flow.Create<int>().Select(x => x + 1));

            var task =
                RunnableGraph.FromGraph(GraphDsl.Create(shuffler, shuffler, shuffler, Sink.First<IEnumerable<int>>(),
                    ValueTuple.Create,
                    (b, s1, s2, s3, sink) =>
                    {
                        var merge = b.Add(new Merge<int>(2));

                        b.From(source(1)).To(s1.In1);
                        b.From(source(10)).To(s1.In2);

                        b.From(s1.Out1).To(s2.In1);
                        b.From(s1.Out2).To(s2.In2);

                        b.From(s2.Out1).To(s3.In1);
                        b.From(s2.Out2).To(s3.In2);

                        b.From(s3.Out1).To(merge.In(0));
                        b.From(s3.Out2).To(merge.In(1));

                        b.From(merge.Out).Via(Flow.Create<int>().Grouped(1000)).To(sink);

                        return ClosedShape.Instance;
                    })).Run(Materializer).Item4;

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {4, 5, 6, 13, 14, 15});
        }
    }
}
