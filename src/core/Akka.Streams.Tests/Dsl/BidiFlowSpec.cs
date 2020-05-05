//-----------------------------------------------------------------------
// <copyright file="BidiFlowSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class BidiFlowSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public BidiFlowSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static BidiFlow<int, long, ByteString, string, NotUsed> Bidi()
        {
            return
                BidiFlow.FromFlows(
                    Flow.Create<int>().Select(x => ((long) x) + 2).WithAttributes(Attributes.CreateName("top")),
                    Flow.Create<ByteString>()
                        .Select(x => x.ToString(Encoding.UTF8))
                        .WithAttributes(Attributes.CreateName("bottom")));
        }

        private static BidiFlow<long, int, string, ByteString, NotUsed> Inverse()
        {
            return
                BidiFlow.FromFlows(
                    Flow.Create<long>().Select(x => ((int)x) + 2).WithAttributes(Attributes.CreateName("top")),
                    Flow.Create<string>()
                        .Select(ByteString.FromString)
                        .WithAttributes(Attributes.CreateName("bottom")));
        }

        private static BidiFlow<int, long, ByteString, string, Task<int>> BidiMaterialized()
        {
            return BidiFlow.FromGraph(GraphDsl.Create(Sink.First<int>(), (b, s) =>
            {
                b.From(Source.Single(42).MapMaterializedValue(_=>Task.FromResult(0))).To(s);

                var top = b.Add(Flow.Create<int>().Select(x => ((long) x) + 2));
                var bottom = b.Add(Flow.Create<ByteString>().Select(x => x.ToString(Encoding.UTF8)));
                return new BidiShape<int,long,ByteString, string>(top.Inlet, top.Outlet, bottom.Inlet, bottom.Outlet);
            }));
        }

        private const string String = "Hello World";
        private static readonly ByteString Bytes = ByteString.FromString(String);


        [Fact]
        public void A_BidiFlow_must_work_top_and_bottom_in_isolation()
        {
            var t = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<long>(), Sink.First<string>(), Keep.Both,
                (b, st, sb) =>
                {
                    var s = b.Add(Bidi());
                    b.From(
                        Source.Single(1)
                            .MapMaterializedValue(_ => (Task.FromResult(1L), Task.FromResult(""))))
                        .To(s.Inlet1);
                    b.From(s.Outlet1).To(st);
                    b.To(sb).From(s.Outlet2);
                    b.To(s.Inlet2)
                        .From(
                            Source.Single(Bytes)
                                .MapMaterializedValue(_ => (Task.FromResult(1L), Task.FromResult(""))));

                    return ClosedShape.Instance;
                })).Run(Materializer);

            var top = t.Item1;
            var bottom = t.Item2;

            top.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue(); 
            bottom.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue(); 
            top.Result.Should().Be(3);
            bottom.Result.Should().Be(String);
        }

        [Fact]
        public void A_BidiFlow_must_work_as_a_Flow_that_is_open_to_the_left()
        {
            var f = Bidi().Join(Flow.Create<long>().Select(x => ByteString.FromString($"Hello {x}")));
            var result = Source.From(Enumerable.Range(1, 3)).Via(f).Limit(10).RunWith(Sink.Seq<string>(), Materializer);
            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.ShouldAllBeEquivalentTo(new[] {"Hello 3", "Hello 4", "Hello 5"});
        }

        [Fact]
        public void A_BidiFlow_must_work_as_a_Flow_that_is_open_on_the_right()
        {
            var f = Flow.Create<string>().Select(int.Parse).Join(Bidi());
            var result =
                Source.From(new[] {ByteString.FromString("1"), ByteString.FromString("2")})
                    .Via(f)
                    .Limit(10)
                    .RunWith(Sink.Seq<long>(), Materializer);
            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.ShouldAllBeEquivalentTo(new[] {3L, 4L});
        }

        [Fact]
        public void A_BidiFlow_must_work_when_atop_its_inverse()
        {
            var f = Bidi().Atop(Inverse()).Join(Flow.Create<int>().Select(x => x.ToString()));
            var result = Source.From(Enumerable.Range(1, 3)).Via(f).Limit(10).RunWith(Sink.Seq<string>(), Materializer);
            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.ShouldAllBeEquivalentTo(new[] { "5", "6", "7" });
        }

        [Fact]
        public void A_BidiFlow_must_work_when_reversed()
        {
            // just reversed from the case above; observe that Flow inverts itself automatically by being on the left side
            var f = Flow.Create<int>().Select(x => x.ToString()).Join(Inverse().Reversed()).Join(Bidi().Reversed());
            var result = Source.From(Enumerable.Range(1, 3)).Via(f).Limit(10).RunWith(Sink.Seq<string>(), Materializer);
            result.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            result.Result.ShouldAllBeEquivalentTo(new[] { "5", "6", "7" });
        }

        [Fact]
        public void A_BidiFlow_must_materialize_its_value()
        {
            var f = RunnableGraph.FromGraph(GraphDsl.Create(BidiMaterialized(), (b, bidi) =>
            {
                var flow1 = b.Add(Flow.Create<string>().Select(int.Parse).MapMaterializedValue(_ => Task.FromResult(0)));
                var flow2 =
                    b.Add(
                        Flow.Create<long>()
                            .Select(x => ByteString.FromString($"Hello {x}"))
                            .MapMaterializedValue(_ => Task.FromResult(0)));
                
                b.AddEdge(flow1.Outlet, bidi.Inlet1);
                b.AddEdge(bidi.Outlet2, flow1.Inlet);

                b.AddEdge(bidi.Outlet1, flow2.Inlet);
                b.AddEdge(flow2.Outlet, bidi.Inlet2);

                return ClosedShape.Instance;
            })).Run(Materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f.Result.Should().Be(42);
        }

        [Fact]
        public void A_BidiFlow_must_combine_materialization_values()
        {
            this.AssertAllStagesStopped(() =>
            {
                var left = Flow.FromGraph(GraphDsl.Create(Sink.First<int>(), (b, sink) =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    var merge = b.Add(new Merge<int>(2));
                    var flow = b.Add(Flow.Create<string>().Select(int.Parse));
                    b.From(broadcast).To(sink);
                    b.From(Source.Single(1).MapMaterializedValue(_ => Task.FromResult(0))).Via(broadcast).To(merge);
                    b.From(flow).To(merge);
                    return new FlowShape<string, int>(flow.Inlet, merge.Out);
                }));

                var right = Flow.FromGraph(GraphDsl.Create(Sink.First<List<long>>(), (b, sink) =>
                {
                    var flow = b.Add(Flow.Create<long>().Grouped(10));
                    var source = b.Add(Source.Single(ByteString.FromString("10")));
                    b.From(flow).To(sink);

                    return new FlowShape<long, ByteString>(flow.Inlet, source.Outlet);
                }));

                var tt = left.JoinMaterialized(BidiMaterialized(), Keep.Both)
                    .JoinMaterialized(right, Keep.Both)
                    .Run(Materializer);
                var t = tt.Item1;
                var l = t.Item1;
                var m = t.Item2;
                var r = tt.Item2;

                Task.WhenAll(l, m, r).Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                l.Result.Should().Be(1);
                m.Result.Should().Be(42);
                r.Result.ShouldAllBeEquivalentTo(new [] {3L, 12L});
            }, Materializer);
        }

        [Fact]
        public void A_BidiFlow_must_suitably_override_attribute_handling_methods()
        {
            var b = (BidiFlow<int, long, ByteString, string, NotUsed>)
                Bidi().WithAttributes(Attributes.CreateName("")).Async().Named("name");

            b.Module.Attributes.GetFirstAttribute<Attributes.Name>().Value.Should().Be("name");
            b.Module.Attributes.GetFirstAttribute<Attributes.AsyncBoundary>()
                .Should()
                .Be(Attributes.AsyncBoundary.Instance);
        }
    }
}
