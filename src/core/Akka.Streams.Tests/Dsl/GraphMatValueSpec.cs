//-----------------------------------------------------------------------
// <copyright file="GraphMatValueSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using Akka.Actor;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphMatValueSpec : AkkaSpec
    {
        public GraphMatValueSpec(ITestOutputHelper helper) : base(helper) { }

        private static Sink<int, Task<int>> FoldSink => Sink.Aggregate<int,int>(0, (sum, i) => sum + i);
        
        private IMaterializer CreateMaterializer(bool autoFusing)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16).WithAutoFusing(autoFusing);
            return Sys.Materializer(settings);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_expose_the_materialized_value_as_source(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

            var sub = this.CreateManualSubscriberProbe<int>();
            var f = RunnableGraph.FromGraph(GraphDsl.Create(FoldSink, (b, fold) =>
            {
                var source = Source.From(Enumerable.Range(1, 10)).MapMaterializedValue(_ => Task.FromResult(0));
                b.From(source).To(fold);
                b.From(b.MaterializedValue)
                    .Via(Flow.Create<Task<int>>().SelectAsync(4, x => x))
                    .To(Sink.FromSubscriber(sub).MapMaterializedValue(_ => Task.FromResult(0)));
                return ClosedShape.Instance;
            })).Run(materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            var r1 = f.Result;
            sub.ExpectSubscription().Request(1);
            var r2 = sub.ExpectNext();
            r1.Should().Be(r2);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_expose_the_materialized_value_as_source_multiple_times(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

            var sub = this.CreateManualSubscriberProbe<int>();
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
            })).Run(materializer);

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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_allow_exposing_the_materialized_value_as_port(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

            var t =
                FoldFeedbackSource.SelectAsync(4, x => x)
                    .Select(x => x + 100)
                    .ToMaterialized(Sink.First<int>(), Keep.Both)
                    .Run(materializer);
            var f1 = t.Item1;
            var f2 = t.Item2;
            f1.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f1.Result.Should().Be(55);

            f2.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f2.Result.Should().Be(155);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_allow_exposing_the_materialized_values_as_port_even_if_wrapped_and_the_final_materialized_value_is_unit(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

            var noMatSource =
                FoldFeedbackSource.SelectAsync(4, x => x).Select(x => x + 100).MapMaterializedValue(_ => NotUsed.Instance);
            var t = noMatSource.RunWith(Sink.First<int>(), materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().Be(155);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_work_properly_with_nesting_and_reusing(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

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

            var t = compositeSource2.ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            var task = Task.WhenAll(t.Item1.Item1.Item1, t.Item1.Item1.Item2, t.Item1.Item2.Item1, t.Item1.Item2.Item2, t.Item2);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result[0].Should().Be(55);
            task.Result[1].Should().Be(55);
            task.Result[2].Should().Be(55);
            task.Result[3].Should().Be(55);
            task.Result[4].Should().Be(55555555);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_work_also_when_the_sources_module_is_copied(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);

            var foldFlow = Flow.FromGraph(GraphDsl.Create(Sink.Aggregate<int, int>(0, (sum, i) => sum + i), (b, fold) =>
            {
                var o = b.From(b.MaterializedValue).Via(Flow.Create<Task<int>>().SelectAsync(4, x => x));
                return new FlowShape<int,int>(fold.Inlet, o.Out);
            }));

            var t = Source.From(Enumerable.Range(1, 10)).Via(foldFlow).RunWith(Sink.First<int>(), materializer);
            t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Result.Should().Be(55);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_perform_side_effecting_transformations_even_when_not_used_as_source(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);
            var done = false;

            var g = GraphDsl.Create(b =>
            {
                b.From(Source.Empty<int>().MapMaterializedValue(_ =>
                {
                    done = true;
                    return _;
                })).To(Sink.Ignore<int>().MapMaterializedValue(_ => NotUsed.Instance));
                return ClosedShape.Instance;
            });
            var r = RunnableGraph.FromGraph(GraphDsl.Create(Sink.Ignore<int>(), (b, sink) =>
            {
                var source = Source.From(Enumerable.Range(1, 10)).MapMaterializedValue(_ => Task.FromResult(0));
                b.Add(g);
                b.From(source).To(sink);
                return ClosedShape.Instance;
            }));

            var task = r.Run(materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            done.Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_produce_NotUsed_when_not_importing_materialized_value(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);
            var source = Source.FromGraph(GraphDsl.Create(b => new SourceShape<NotUsed>(b.MaterializedValue)));
            var task = source.RunWith(Sink.Seq<NotUsed>(), materializer);
            task.Wait(TimeSpan.FromSeconds(3));
            task.Result.ShouldAllBeEquivalentTo(NotUsed.Instance);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_produce_NotUsed_when_starting_from_Flow_Via_with_transformation(bool autoFusing)
        {
            var materializer = CreateMaterializer(autoFusing);
            var done = false;
            Source.Empty<int>().ViaMaterialized(Flow.Create<int>().Via(Flow.Create<int>().MapMaterializedValue(_ =>
            {
                done = true;
                return _;
            })), Keep.Right).To(Sink.Ignore<int>()).Run(materializer).Should().Be(NotUsed.Instance);
            done.Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_ignore_materialized_values_for_a_graph_with_no_materialized_values_exposed(bool autoFusing)
        {
            // The bug here was that the default behavior for "compose" in Module is Keep.left, but
            // EmptyModule.compose broke this by always returning the other module intact, which means
            // that the materialized value was that of the other module (which is inconsistent with Keep.left behavior).

            var sink = Sink.Ignore<int>();

            var g = RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var s = b.Add(sink);
                var source = b.Add(Source.From(Enumerable.Range(1, 3)));
                var flow = b.Add(Flow.Create<int>());

                b.From(source).Via(flow).To(s);
                return ClosedShape.Instance;
            }));

            var result = g.Run(CreateMaterializer(autoFusing));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void A_Graph_with_materialized_value_must_ignore_materialized_values_for_a_graph_with_no_materialized_values_exposed_but_keep_side_effects(bool autoFusing)
        {
            // The bug here was that the default behavior for "compose" in Module is Keep.left, but
            // EmptyModule.compose broke this by always returning the other module intact, which means
            // that the materialized value was that of the other module (which is inconsistent with Keep.left behavior).

            var sink = Sink.Ignore<int>().MapMaterializedValue(_=>
            {
                TestActor.Tell("side effect!");
                return _;
            });

            var g = RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var s = b.Add(sink);
                var source = b.Add(Source.From(Enumerable.Range(1, 3)));
                var flow = b.Add(Flow.Create<int>());

                b.From(source).Via(flow).To(s);
                return ClosedShape.Instance;
            }));

            var result = g.Run(CreateMaterializer(autoFusing));

            ExpectMsg("side effect!");
        }

        [Fact]
        public void A_Graph_with_Identity_Flow_optimization_even_if_port_are_wired_in_an_arbitrary_higher_nesting_level()
        {
            var mat2 = Sys.Materializer(ActorMaterializerSettings.Create(Sys).WithAutoFusing(false));

            var subFlow = GraphDsl.Create(b =>
            {
                var zip = b.Add(new Zip<string, string>());
                var bc = b.Add(new Broadcast<string>(2));

                b.From(bc.Out(0)).To(zip.In0);
                b.From(bc.Out(1)).To(zip.In1);

                return new FlowShape<string, (string, string)>(bc.In, zip.Out);
            }).Named("NestedFlow");

            var nest1 = Flow.Create<string>().Via(subFlow);
            var nest2 = Flow.Create<string>().Via(nest1);
            var nest3 = Flow.Create<string>().Via(nest2);
            var nest4 = Flow.Create<string>().Via(nest3);

            //fails
            var matValue = Source.Single("").Via(nest4).To(Sink.Ignore<(string, string)>()).Run(mat2);
            matValue.Should().Be(NotUsed.Instance);
        }

        [Fact]
        public void A_Graph_must_not_ignore_materialized_value_of_identity_flow_which_is_optimized_away()
        {
            var mat = Sys.Materializer(ActorMaterializerSettings.Create(Sys).WithAutoFusing(false));
            var t = Source.Single(1)
                .ViaMaterialized(Flow.Identity<int>(), Keep.Both)
                .To(Sink.Ignore<int>())
                .Run(mat);

            t.Item1.ShouldBe(NotUsed.Instance);
            t.Item2.ShouldBe(NotUsed.Instance);

            var m1 = Source.Maybe<int>()
                .ViaMaterialized(Flow.Identity<int>(), Keep.Left)
                .To(Sink.Ignore<int>())
                .Run(mat);
            m1.SetResult(0);

            var m2 = Source.Single(1)
                .ViaMaterialized(Flow.Identity<int>(), Keep.Right)
                .To(Sink.Ignore<int>())
                .Run(mat);
            m2.ShouldBe(NotUsed.Instance);
        }
    }
}

