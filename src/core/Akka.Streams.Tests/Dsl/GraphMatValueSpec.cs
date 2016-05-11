//-----------------------------------------------------------------------
// <copyright file="GraphMatValueSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Reactive.Streams;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMatValueSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphMatValueSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static Sink<int, Task<int>> FoldSink => Sink.Aggregate<int,int>(0, (sum, i) => sum + i);

        [Fact]
        public void A_Graph_with_materialized_value_must_expose_the_materialized_value_as_source()
        {
            var sub = TestSubscriber.CreateManualProbe<int>(this);
            var f = RunnableGraph.FromGraph(GraphDsl.Create(FoldSink, (b, fold) =>
            {
                var source = Source.From(Enumerable.Range(1, 10)).MapMaterializedValue(_ => Task.FromResult(0));
                b.From(source).To(fold);
                b.From(b.MaterializedValue)
                    .Via(Flow.Create<Task<int>>().SelectAsync(4, x => x))
                    .To(Sink.FromSubscriber(sub).MapMaterializedValue(_ => Task.FromResult(0)));
                return ClosedShape.Instance;
            })).Run(Materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var r1 = f.Result;
            sub.ExpectSubscription().Request(1);
            var r2 = sub.ExpectNext();
            r1.Should().Be(r2);
        }

        [Fact]
        public void A_Graph_with_materialized_value_must_expose_the_materialized_value_as_source_multiple_times()
        {
            var sub = TestSubscriber.CreateManualProbe<int>(this);
            var f = RunnableGraph.FromGraph(GraphDsl.Create(FoldSink, (b, fold) =>
            {
                var zip = b.Add(new ZipWith<int, int, int>((i, i1) => i + i1));
                var source = Source.From(Enumerable.Range(1, 10)).MapMaterializedValue(_ => Task.FromResult(0));
                b.From(source).To(fold);
                b.From(b.MaterializedValue)
                    .Via(Flow.Create<Task<int>>().SelectAsync(4, x => x))
                    .To(zip.In0);
                b.From(b.MaterializedValue)
                    .Via(Flow.Create<Task<int>>().SelectAsync(4, x => x))
                    .To(zip.In1);

                b.From(zip.Out).To(Sink.FromSubscriber(sub).MapMaterializedValue(_ => Task.FromResult(0)));
                return ClosedShape.Instance;
            })).Run(Materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var r1 = f.Result;
            sub.ExpectSubscription().Request(1);
            var r2 = sub.ExpectNext();
            r1.Should().Be(r2/2);
        }

        // Exposes the materialized value as a stream value
        private static Source<Task<int>, Task<int>> FoldFeedbackSource => Source.FromGraph(GraphDsl.Create(FoldSink,
            (b, fold) =>
            {
                var source = Source.From(Enumerable.Range(1, 10));
                var shape = b.Add(source);
                b.From(shape).To(fold);
                return new SourceShape<Task<int>>(b.MaterializedValue);
            }));

        [Fact]
        public void A_Graph_with_materialized_value_must_allow_exposing_the_materialized_value_as_port()
        {
            var t =
                FoldFeedbackSource.SelectAsync(4, x => x)
                    .Select(x => x + 100)
                    .ToMaterialized(Sink.First<int>(), Keep.Both)
                    .Run(Materializer);
            var f1 = t.Item1;
            var f2 = t.Item2;
            f1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f1.Result.Should().Be(55);

            f2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f2.Result.Should().Be(155);
        }

        [Fact]
        public void A_Graph_with_materialized_value_must_allow_exposing_the_materialized_values_as_port_even_if_wrapped_and_the_final_materialized_value_is_unit()
        {
            var noMatSource =
                FoldFeedbackSource.SelectAsync(4, x => x).Select(x => x + 100).MapMaterializedValue(_ => NotUsed.Instance);
            var t = noMatSource.RunWith(Sink.First<int>(), Materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().Be(155);
        }

        [Fact]
        public void A_Graph_with_materialized_value_must_work_properly_with_nesting_and_reusing()
        {
            var compositeSource1 = Source.FromGraph(GraphDsl.Create(FoldFeedbackSource, FoldFeedbackSource, Keep.Both,
                (b, s1, s2) =>
                {
                    var zip = b.Add(new ZipWith<int, int, int>((i, i1) => i + i1));

                    b.From(s1.Outlet).Via(Flow.Create<Task<int>>().SelectAsync(4, x => x)).To(zip.In0);
                    b.From(s2.Outlet).Via(Flow.Create<Task<int>>().SelectAsync(4, x => x).Select(x => x*100)).To(zip.In1);
                    
                    return new SourceShape<int>(zip.Out);
                }));

            var compositeSource2 = Source.FromGraph(GraphDsl.Create(compositeSource1, compositeSource1, Keep.Both,
                (b, s1, s2) =>
                {
                    var zip = b.Add(new ZipWith<int, int, int>((i, i1) => i + i1));

                    b.From(s1.Outlet).To(zip.In0);
                    b.From(s2.Outlet).Via(Flow.Create<int>().Select(x => x*10000)).To(zip.In1);

                    return new SourceShape<int>(zip.Out);
                }));

            var t = compositeSource2.ToMaterialized(Sink.First<int>(), Keep.Both).Run(Materializer);
            var task = Task.WhenAll(t.Item1.Item1.Item1, t.Item1.Item1.Item2, t.Item1.Item2.Item1, t.Item1.Item2.Item2, t.Item2);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result[0].Should().Be(55);
            task.Result[1].Should().Be(55);
            task.Result[2].Should().Be(55);
            task.Result[3].Should().Be(55);
            task.Result[4].Should().Be(55555555);
        }

        [Fact]
        public void A_Graph_with_materialized_value_must_work_also_when_the_sources_module_is_copied()
        {
            var foldFlow = Flow.FromGraph(GraphDsl.Create(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), (b, fold) =>
            {
                var o = b.From(b.MaterializedValue).Via(Flow.Create<Task<int>>().SelectAsync(4, x => x));
                return new FlowShape<int,int>(fold.Inlet, o.Out);
            }));

            var t = Source.From(Enumerable.Range(1, 10)).Via(foldFlow).RunWith(Sink.First<int>(), Materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().Be(55);
        }
    }
}

