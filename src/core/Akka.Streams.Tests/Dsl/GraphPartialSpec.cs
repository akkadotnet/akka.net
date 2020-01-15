//-----------------------------------------------------------------------
// <copyright file="GraphPartialSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{

    public class GraphPartialSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphPartialSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void GraphDSLs_Partial_must_be_able_to_build_and_reuse_simple_partial_graphs()
        {
            var doubler = GraphDsl.Create(b =>
            {
                var broadcast = b.Add(new Broadcast<int>(2));
                var zip = b.Add(ZipWith.Apply((int i, int i1) => i + i1));

                b.From(broadcast.Out(0)).To(zip.In0);
                b.From(broadcast.Out(1)).To(zip.In1);

                return new FlowShape<int, int>(broadcast.In, zip.Out);
            });

            var task =
                RunnableGraph.FromGraph(GraphDsl.Create(doubler, doubler, Sink.First<IEnumerable<int>>(), ValueTuple.Create,
                    (b, d1, d2, sink) =>
                    {
                        var source =
                            Source.From(Enumerable.Range(1, 3))
                                .MapMaterializedValue(_ => default((NotUsed, NotUsed, Task<IEnumerable<int>>)));

                        b.From(source).To(d1.Inlet);
                        b.From(d1.Outlet).To(d2.Inlet);
                        b.From(d2.Outlet).Via(Flow.Create<int>().Grouped(100)).To(sink.Inlet);

                        return ClosedShape.Instance;
                    })).Run(Materializer).Item3;

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {4, 8, 12});
        }

        [Fact]
        public void GraphDSLs_Partial_must_be_able_to_build_and_reuse_simple_materializing_partial_graphs()
        {
            var doubler = GraphDsl.Create(Sink.First<IEnumerable<int>>(), (b, sink) =>
            {
                var broadcast = b.Add(new Broadcast<int>(3));
                var zip = b.Add(ZipWith.Apply((int i, int i1) => i + i1));

                b.From(broadcast.Out(0)).To(zip.In0);
                b.From(broadcast.Out(1)).To(zip.In1);
                b.From(broadcast.Out(2)).Via(Flow.Create<int>().Grouped(100)).To(sink.Inlet);

                return new FlowShape<int, int>(broadcast.In, zip.Out);
            });

            var t =
                RunnableGraph.FromGraph(GraphDsl.Create(doubler, doubler, Sink.First<IEnumerable<int>>(), ValueTuple.Create,
                    (b, d1, d2, sink) =>
                    {
                        var source =
                            Source.From(Enumerable.Range(1, 3))
                                .MapMaterializedValue(_ => default((Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>)));

                        b.From(source).To(d1.Inlet);
                        b.From(d1.Outlet).To(d2.Inlet);
                        b.From(d2.Outlet).Via(Flow.Create<int>().Grouped(100)).To(sink.Inlet);

                        return ClosedShape.Instance;
                    })).Run(Materializer);

            var task = Task.WhenAll(t.Item1, t.Item2, t.Item3);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result[0].ShouldAllBeEquivalentTo(new[] {1, 2, 3});
            task.Result[1].ShouldAllBeEquivalentTo(new[] {2, 4, 6});
            task.Result[2].ShouldAllBeEquivalentTo(new[] { 4, 8, 12 });
        }

        [Fact]
        public void GraphDSLs_Partial_must_be_able_to_build_and_reuse_complex_materializing_partial_graphs()
        {
            var summer = Sink.Aggregate<int, int>(0, (i, i1) => i + i1);

            var doubler = GraphDsl.Create(summer, summer, ValueTuple.Create, (b, s1,s2) =>
            {
                var broadcast = b.Add(new Broadcast<int>(3));
                var broadcast2 = b.Add(new Broadcast<int>(2));
                var zip = b.Add(ZipWith.Apply((int i, int i1) => i + i1));

                b.From(broadcast.Out(0)).To(zip.In0);
                b.From(broadcast.Out(1)).To(zip.In1);
                b.From(broadcast.Out(2)).To(s1.Inlet);

                b.From(zip.Out).To(broadcast2.In);
                b.From(broadcast2.Out(0)).To(s2.Inlet);

                return new FlowShape<int, int>(broadcast.In, broadcast2.Out(1));
            });

            var t =
                RunnableGraph.FromGraph(GraphDsl.Create(doubler, doubler, Sink.First<IEnumerable<int>>(), ValueTuple.Create,
                    (b, d1, d2, sink) =>
                    {
                        var source =
                            Source.From(Enumerable.Range(1, 3))
                                .MapMaterializedValue(_ => default(((Task<int>, Task<int>), (Task<int>, Task<int>), Task<IEnumerable<int>>)));

                        b.From(source).To(d1.Inlet);
                        b.From(d1.Outlet).To(d2.Inlet);
                        b.From(d2.Outlet).Via(Flow.Create<int>().Grouped(100)).To(sink.Inlet);

                        return ClosedShape.Instance;
                    })).Run(Materializer);

            var task = Task.WhenAll(t.Item1.Item1, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(new[] {6, 12, 12, 24});
            t.Item3.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            t.Item3.Result.ShouldAllBeEquivalentTo(new [] {4, 8, 12});
        }

        [Fact]
        public void GraphDSLs_Partial_must_be_able_to_expose_the_ports_of_imported_graphs()
        {
            var p = GraphDsl.Create(Flow.Create<int>().Select(x => x + 1),
                (b, flow) => new FlowShape<int, int>(flow.Inlet, flow.Outlet));

            var task = RunnableGraph.FromGraph(GraphDsl.Create(Sink.First<int>(), p, Keep.Left,
                (b, sink, flow) =>
                {
                    var source = Source.Single(0).MapMaterializedValue<Task<int>>(_ => null);

                    b.From(source).To(flow.Inlet);
                    b.From(flow.Outlet).To(sink.Inlet);

                    return ClosedShape.Instance;
                })).Run(Materializer);

            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.Should().Be(1);
        }
    }
}
