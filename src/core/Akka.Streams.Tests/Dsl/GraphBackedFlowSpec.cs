//-----------------------------------------------------------------------
// <copyright file="GraphBackedFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphBackedFlowSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphBackedFlowSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static Source<int, NotUsed> Source1 => Source.From(Enumerable.Range(0, 4));

        private static IGraph<FlowShape<int, string>, NotUsed> PartialGraph()
        {
            return GraphDsl.Create(b =>
            {
                var source2 = Source.From(Enumerable.Range(4, 6));
                var source3 = Source.Empty<int>();
                var source4 = Source.Empty<string>();

                var inMerge = b.Add(new Merge<int>(2));
                var outMerge = b.Add(new Merge<string>(2));
                var m2 = b.Add(new Merge<int>(2));

                b.From(inMerge.Out).Via(Flow.Create<int>().Select(x => x * 2)).To(m2.In(0));
                b.From(m2.Out).Via(Flow.Create<int>().Select(x => x / 2).Select(i => (i + 1).ToString())).To(outMerge.In(0));

                b.From(source2).To(inMerge.In(0));
                b.From(source3).To(m2.In(1));
                b.From(source4).To(outMerge.In(1));
                return new FlowShape<int, string>(inMerge.In(1), outMerge.Out);
            });
        }

        private const int StandardRequests = 10;
        private static IEnumerable<int> StandardResult => Enumerable.Range(1, 10);

        private static void ValidateProbe(TestSubscriber.ManualProbe<int> probe, int requests, IEnumerable<int> result)
        {
            var subscription = probe.ExpectSubscription();

            var collected = Enumerable.Range(1, requests).Select(_ =>
            {
                subscription.Request(1);
                return probe.ExpectNext();
            });

            collected.ShouldAllBeEquivalentTo(result);
            probe.ExpectComplete();
        }

        [Fact]
        public void GraphDSLs_when_turned_into_flows_should_work_with_a_Source_and_Sink()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var flow = Flow.FromGraph(GraphDsl.Create(PartialGraph(), (b, partial) =>
            {
                var o = b.From(partial.Outlet).Via(Flow.Create<string>().Select(int.Parse));
                return new FlowShape<int, int>(partial.Inlet, o.Out);
            }));

            Source1.Via(flow).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe,StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_flows_should_be_transformable_with_a_Pipe()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var flow =
                Flow.FromGraph(GraphDsl.Create(PartialGraph(),
                    (b, partial) => new FlowShape<int, string>(partial.Inlet, partial.Outlet)));

            Source1.Via(flow).Select(int.Parse).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_flows_should_work_with_another_GraphFlow()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var flow1 =
                Flow.FromGraph(GraphDsl.Create(PartialGraph(),
                    (b, partial) => new FlowShape<int, string>(partial.Inlet, partial.Outlet)));

            var flow2 =
                Flow.FromGraph(GraphDsl.Create(Flow.Create<string>().Select(int.Parse),
                    (b, importFlow) => new FlowShape<string, int>(importFlow.Inlet, importFlow.Outlet)));

            Source1.Via(flow1).Via(flow2).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_flows_should_be_reusable_multiple_times()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var flow =
                Flow.FromGraph(GraphDsl.Create(Flow.Create<int>().Select(x=>x*2),
                    (b, importFlow) => new FlowShape<int, int>(importFlow.Inlet, importFlow.Outlet)));

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var source = Source.From(Enumerable.Range(1, 5));
                b.From(source).Via(flow).Via(flow).To(Sink.FromSubscriber(probe));
                return ClosedShape.Instance;
            })).Run(Materializer);

            ValidateProbe(probe, 5, new[] {4, 8, 12, 16, 20});
        }

        

        [Fact]
        public void GraphDSLs_when_turned_into_sources_should_work_with_a_Sink()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var source = Source.FromGraph(GraphDsl.Create(PartialGraph(), (b, partial) =>
            {
                b.From(Source1).To(partial.Inlet);
                var o = b.From(partial.Outlet).Via(Flow.Create<string>().Select(int.Parse));
                return new SourceShape<int>(o.Out);
            }));

            source.To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sources_should_work_with_a_Sink_when_having_KeyedSource_inside()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            var source = Source.AsSubscriber<int>();
            var mm = source.To(Sink.FromSubscriber(probe)).Run(Materializer);
            Source1.To(Sink.FromSubscriber(mm)).Run(Materializer);

            ValidateProbe(probe, 4, new[] {0, 1, 2, 3});
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sources_should_be_transformable_with_a_Pipe()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var source =
                Source.FromGraph(GraphDsl.Create(PartialGraph(),
                    (b, partial) =>
                    {
                        b.From(Source1).To(partial.Inlet);
                        return new SourceShape<string>(partial.Outlet);
                    }));

            source.Select(int.Parse).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sources_should_work_with_an_GraphFlow()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var source =
                Source.FromGraph(GraphDsl.Create(PartialGraph(),
                    (b, partial) =>
                    {
                        b.From(Source1).To(partial.Inlet);
                        return new SourceShape<string>(partial.Outlet);
                    }));

            var flow =
                Flow.FromGraph(GraphDsl.Create(Flow.Create<string>().Select(int.Parse),
                    (b, importFlow) => new FlowShape<string, int>(importFlow.Inlet, importFlow.Outlet)));

            source.Via(flow).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sources_should_be_reusable_multiple_times()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var source =
                Source.FromGraph(GraphDsl.Create(Source.From(Enumerable.Range(1, 5)),
                    (b, s) =>
                    {
                        var o = b.From(s.Outlet).Via(Flow.Create<int>().Select(x => x*2));
                        return new SourceShape<int>(o.Out);
                    }));

            RunnableGraph.FromGraph(GraphDsl.Create(source, source, Keep.Both, (b, s1, s2) =>
            {
                var merge = b.Add(new Merge<int>(2));
                b.From(s1.Outlet).To(merge.In(0));
                b.From(merge.Out)
                    .To(Sink.FromSubscriber(probe).MapMaterializedValue(_ => (NotUsed.Instance, NotUsed.Instance)));
                b.From(s2.Outlet).Via(Flow.Create<int>().Select(x => x*10)).To(merge.In(1));
                return ClosedShape.Instance;
            })).Run(Materializer);

            ValidateProbe(probe, 10, new[] {2, 4, 6, 8, 10, 20, 40, 60, 80, 100});
        }

        

        [Fact]
        public void GraphDSLs_when_turned_into_sinks_should_work_with_a_Source()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var sink = Sink.FromGraph(GraphDsl.Create(PartialGraph(), (b, partial) =>
            {
                b.From(partial.Outlet).Via(Flow.Create<string>().Select(int.Parse)).To(Sink.FromSubscriber(probe));
                return new SinkShape<int>(partial.Inlet);
            }));

            Source1.To(sink).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sinks_should_work_with_a_Source_when_having_KeyedSink_inside()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            var pubSink = Sink.AsPublisher<int>(false);

            var sink = Sink.FromGraph(GraphDsl.Create(pubSink, (b, p) => new SinkShape<int>(p.Inlet)));
            var mm = Source1.RunWith(sink, Materializer);
            Source.FromPublisher(mm).To(Sink.FromSubscriber(probe)).Run(Materializer);

            ValidateProbe(probe, 4, new[] { 0, 1, 2, 3 });
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sinks_should_be_transformable_with_a_Pipe()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var sink =
                Sink.FromGraph(GraphDsl.Create(PartialGraph(), Flow.Create<string>().Select(int.Parse), Keep.Both,
                    (b, partial, flow) =>
                    {
                        var s = Sink.FromSubscriber(probe)
                            .MapMaterializedValue(_ => (NotUsed.Instance, NotUsed.Instance));

                        b.From(flow.Outlet).To(partial.Inlet);
                        b.From(partial.Outlet).Via(Flow.Create<string>().Select(int.Parse)).To(s);
                        return new SinkShape<string>(flow.Inlet);
                    }));

            var iSink = Flow.Create<int>().Select(i => i.ToString()).To(sink);
            Source1.To(iSink).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_turned_into_sinks_should_work_with_a_GraphFlow()
        {
            var probe = this.CreateManualSubscriberProbe<int>();

            var flow =
                Flow.FromGraph(GraphDsl.Create(PartialGraph(),
                    (b, partial) => new FlowShape<int, string>(partial.Inlet, partial.Outlet)));

            var sink = Sink.FromGraph(GraphDsl.Create(Flow.Create<string>().Select(int.Parse), (b, f) =>
            {
                b.From(f.Outlet).To(Sink.FromSubscriber(probe));
                return new SinkShape<string>(f.Inlet);
            }));

            Source1.Via(flow).To(sink).Run(Materializer);

            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_used_together_should_materialize_properly()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            var inSource = Source.AsSubscriber<int>();
            var outSink = Sink.AsPublisher<int>(false);

            var flow = Flow.FromGraph(GraphDsl.Create(PartialGraph(), (b, partial) =>
            {
                var o = b.From(partial.Outlet).Via(Flow.Create<string>().Select(int.Parse));
                return new FlowShape<int, int>(partial.Inlet, o.Out);
            }));

            var source =
                Source.FromGraph(GraphDsl.Create(Flow.Create<int>().Select(x => x.ToString()), inSource, Keep.Right,
                    (b, f, src) =>
                    {
                        b.From(src.Outlet).To(f.Inlet);
                        return new SourceShape<string>(f.Outlet);
                    }));

            var sink = Sink.FromGraph(GraphDsl.Create(Flow.Create<string>().Select(int.Parse), outSink, Keep.Right,
                (b, f, s) =>
                {
                    b.From(f.Outlet).To(s.Inlet);
                    return new SinkShape<string>(f.Inlet);
                }));

            var t = RunnableGraph.FromGraph(GraphDsl.Create(source, flow, sink, (src, _, snk) => (src, snk),
                (b, src, f, snk) =>
                {
                    b.From(src.Outlet).Via(Flow.Create<string>().Select(int.Parse)).To(f.Inlet);
                    b.From(f.Outlet).Via(Flow.Create<int>().Select(x => x.ToString())).To(snk.Inlet);
                    return ClosedShape.Instance;
                })).Run(Materializer);

            var subscriber = t.Item1;
            var publisher = t.Item2;

            Source1.RunWith(Sink.AsPublisher<int>(false), Materializer).Subscribe(subscriber);
            publisher.Subscribe(probe);
            ValidateProbe(probe, StandardRequests, StandardResult);
        }

        [Fact]
        public void GraphDSLs_when_used_together_should_allow_connecting_source_to_sink_directly()
        {
            var probe = this.CreateManualSubscriberProbe<int>();
            var inSource = Source.AsSubscriber<int>();
            var outSink = Sink.AsPublisher<int>(false);

            var source = Source.FromGraph(GraphDsl.Create(inSource, (b, src) => new SourceShape<int>(src.Outlet)));

            var sink = Sink.FromGraph(GraphDsl.Create(outSink, (b, s) => new SinkShape<int>(s.Inlet)));

            var t = RunnableGraph.FromGraph(GraphDsl.Create(source, sink, Keep.Both, (b, src, snk) =>
            {
                b.From(src.Outlet).To(snk.Inlet);
                return ClosedShape.Instance;
            })).Run(Materializer);

            var subscriber = t.Item1;
            var publisher = t.Item2;

            Source1.RunWith(Sink.AsPublisher<int>(false), Materializer).Subscribe(subscriber);
            publisher.Subscribe(probe);

            ValidateProbe(probe, 4, new[] {0, 1, 2, 3});
        }
    }
}
