//-----------------------------------------------------------------------
// <copyright file="GraphBroadcastSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
    public class GraphBroadcastSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphBroadcastSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_Broadcast_must_broadcast_to_other_subscriber()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(broadcast.In);
                    b.From(broadcast.Out(0))
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c1));
                    b.From(broadcast.Out(1))
                        .Via(Flow.Create<int>().Buffer(16, OverflowStrategy.Backpressure))
                        .To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(1);
                sub2.Request(2);

                await c1.ExpectNext(1).ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await c2.ExpectNext(1, 2).ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                sub1.Request(3);
                await c1.ExpectNext(2, 3).ExpectCompleteAsync();
                sub2.Request(3);
                await c2.ExpectNext(3).ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_work_with_one_way_broadcast()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var t = Source.FromGraph(GraphDsl.Create(b =>                                                                         
                {                                                                             
                    var broadcast = b.Add(new Broadcast<int>(1));                                                                             
                    var source = b.Add(Source.From(Enumerable.Range(1, 3)));
                                                                             
                    b.From(source).To(broadcast.In);
                                                                             
                    return new SourceShape<int>(broadcast.Out(0));                                                                         
                })).RunAggregate(new List<int>(), (list, i) =>                                                                         
                {                                                                             
                    list.Add(i);                                                                             
                    return list;                                                                         
                }, Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_work_with_n_way_broadcast()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var headSink = Sink.First<IEnumerable<int>>();

                var t = RunnableGraph.FromGraph(GraphDsl.Create(headSink, headSink, headSink, headSink, headSink, ValueTuple.Create,
                    (b, p1, p2, p3, p4, p5) =>
                    {
                        var broadcast = b.Add(new Broadcast<int>(5));
                        var source = b.Add(Source.From(Enumerable.Range(1, 3)));

                        b.From(source).To(broadcast.In);
                        b.From(broadcast.Out(0)).Via(Flow.Create<int>().Grouped(5)).To(p1.Inlet);
                        b.From(broadcast.Out(1)).Via(Flow.Create<int>().Grouped(5)).To(p2.Inlet);
                        b.From(broadcast.Out(2)).Via(Flow.Create<int>().Grouped(5)).To(p3.Inlet);
                        b.From(broadcast.Out(3)).Via(Flow.Create<int>().Grouped(5)).To(p4.Inlet);
                        b.From(broadcast.Out(4)).Via(Flow.Create<int>().Grouped(5)).To(p5.Inlet);
                        return ClosedShape.Instance;
                    })).Run(Materializer);

                var task = Task.WhenAll(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                foreach (var list in task.Result)
                    list.Should().BeEquivalentTo(new[] { 1, 2, 3 });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact(Skip="We don't have enough overloads for GraphDsl.Create")]
        public async Task A_Broadcast_must_with_22_way_broadcast()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                //var headSink = Sink.First<IEnumerable<int>>();

                //var t = RunnableGraph.FromGraph(GraphDsl.Create(headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, headSink, ValueTuple.Create,
                //    (b, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12, p13, p14, p15, p16, p17, p18, p19, p20, p21, p22) =>
                //    {
                //        var broadcast = b.Add(new Broadcast<int>(22));
                //        var source = b.Add(Source.From(Enumerable.Range(1, 3)));

                //        b.From(source).To(broadcast.In);
                //        b.From(broadcast.Out(0)).Via(Flow.Create<int>().Grouped(5)).To(p1.Inlet);
                //        b.From(broadcast.Out(1)).Via(Flow.Create<int>().Grouped(5)).To(p2.Inlet);
                //        b.From(broadcast.Out(2)).Via(Flow.Create<int>().Grouped(5)).To(p3.Inlet);
                //        b.From(broadcast.Out(3)).Via(Flow.Create<int>().Grouped(5)).To(p4.Inlet);
                //        b.From(broadcast.Out(4)).Via(Flow.Create<int>().Grouped(5)).To(p5.Inlet);
                //        b.From(broadcast.Out(5)).Via(Flow.Create<int>().Grouped(5)).To(p6.Inlet);
                //        b.From(broadcast.Out(6)).Via(Flow.Create<int>().Grouped(5)).To(p7.Inlet);
                //        b.From(broadcast.Out(7)).Via(Flow.Create<int>().Grouped(5)).To(p8.Inlet);
                //        b.From(broadcast.Out(8)).Via(Flow.Create<int>().Grouped(5)).To(p9.Inlet);
                //        b.From(broadcast.Out(9)).Via(Flow.Create<int>().Grouped(5)).To(p10.Inlet);
                //        b.From(broadcast.Out(10)).Via(Flow.Create<int>().Grouped(5)).To(p11.Inlet);
                //        b.From(broadcast.Out(11)).Via(Flow.Create<int>().Grouped(5)).To(p12.Inlet);
                //        b.From(broadcast.Out(12)).Via(Flow.Create<int>().Grouped(5)).To(p13.Inlet);
                //        b.From(broadcast.Out(13)).Via(Flow.Create<int>().Grouped(5)).To(p14.Inlet);
                //        b.From(broadcast.Out(14)).Via(Flow.Create<int>().Grouped(5)).To(p15.Inlet);
                //        b.From(broadcast.Out(15)).Via(Flow.Create<int>().Grouped(5)).To(p16.Inlet);
                //        b.From(broadcast.Out(16)).Via(Flow.Create<int>().Grouped(5)).To(p17.Inlet);
                //        b.From(broadcast.Out(17)).Via(Flow.Create<int>().Grouped(5)).To(p18.Inlet);
                //        b.From(broadcast.Out(18)).Via(Flow.Create<int>().Grouped(5)).To(p19.Inlet);
                //        b.From(broadcast.Out(19)).Via(Flow.Create<int>().Grouped(5)).To(p20.Inlet);
                //        b.From(broadcast.Out(20)).Via(Flow.Create<int>().Grouped(5)).To(p21.Inlet);
                //        b.From(broadcast.Out(21)).Via(Flow.Create<int>().Grouped(5)).To(p22.Inlet);
                //        return ClosedShape.Instance;
                //    })).Run(Materializer);

                //var task = Task.WhenAll(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5, t.Item6, t.Item7, t.Item8, t.Item9, t.Item10, t.Item11, t.Item12, t.Item13, t.Item14, t.Item15, t.Item16, t.Item17, t.Item18, t.Item19, t.Item20, t.Item21, t.Item22);
                //task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                //foreach (var list in task.Result)
                //    list.Should().BeEquivalentTo(new[] { 1, 2, 3 });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_produce_to_other_even_though_downstream_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(broadcast.In);
                    b.From(broadcast.Out(0))
                        .Via(Flow.Create<int>())
                        .To(Sink.FromSubscriber(c1));
                    b.From(broadcast.Out(1))
                        .Via(Flow.Create<int>())
                        .To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                sub1.Cancel();
                var sub2 = await c2.ExpectSubscriptionAsync();
                sub2.Request(3);
                c2.ExpectNext(1, 2, 3);
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_produce_to_downstream_even_though_other_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(broadcast.In);
                    b.From(broadcast.Out(0))
                        .Via(Flow.Create<int>().Named("identity-a"))
                        .To(Sink.FromSubscriber(c1));
                    b.From(broadcast.Out(1))
                        .Via(Flow.Create<int>().Named("identity-b"))
                        .To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();
                sub2.Cancel();
                sub1.Request(3);
                c1.ExpectNext(1, 2, 3);
                await c1.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_cancel_upstream_when_downstreams_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p1 = this.CreateManualPublisherProbe<int>();
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    var source = Source.FromPublisher(p1.Publisher);
                    b.From(source).To(broadcast.In);
                    b.From(broadcast.Out(0))
                        .Via(Flow.Create<int>())
                        .To(Sink.FromSubscriber(c1));
                    b.From(broadcast.Out(1))
                        .Via(Flow.Create<int>())
                        .To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var bSub = await p1.ExpectSubscriptionAsync();
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(3);
                sub2.Request(3);
                await p1.ExpectRequestAsync(bSub, 16);
                bSub.SendNext(1);
                await c1.ExpectNextAsync(1);
                await c2.ExpectNextAsync(1);
                bSub.SendNext(2);
                await c1.ExpectNextAsync(2);
                await c2.ExpectNextAsync(2);
                sub1.Cancel();
                sub2.Cancel();
                await bSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                var sink = Sink.FromGraph(GraphDsl.Create(b =>
                {
                    var broadcast = b.Add(new Broadcast<int>(2));
                    b.From(broadcast.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(broadcast.Out(1)).To(Sink.FromSubscriber(c2));
                    return new SinkShape<int>(broadcast.In);
                }));

                var s = Source.AsSubscriber<int>().To(sink).Run(Materializer);

                var up = this.CreateManualPublisherProbe<int>();

                var downSub1 = await c1.ExpectSubscriptionAsync();
                var downSub2 = await c2.ExpectSubscriptionAsync();
                downSub1.Cancel();
                downSub2.Cancel();

                up.Subscribe(s);
                var upSub = await up.ExpectSubscriptionAsync();
                await upSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_AltoTo_must_broadcast()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.SinkProbe<int>();
                var p2 = this.SinkProbe<int>();

                var t =
                    Source.From(Enumerable.Range(1, 6))
                        .AlsoToMaterialized(p, Keep.Right)
                        .ToMaterialized(p2, Keep.Both)
                        .Run(Materializer);

                var ps1 = t.Item1;
                var ps2 = t.Item2;

                await ps1.RequestAsync(6);
                await ps2.RequestAsync(6);
                ps1.ExpectNext(1, 2, 3, 4, 5, 6);
                ps2.ExpectNext(1, 2, 3, 4, 5, 6);
                await ps1.ExpectCompleteAsync();
                await ps2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Broadcast_must_AlsoTo_must_continue_if_sink_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p = this.SinkProbe<int>();
                var p2 = this.SinkProbe<int>();

                var t =
                    Source.From(Enumerable.Range(1, 6))
                        .AlsoToMaterialized(p, Keep.Right)
                        .ToMaterialized(p2, Keep.Both)
                        .Run(Materializer);

                var ps1 = t.Item1;
                var ps2 = t.Item2;

                await ps2.RequestAsync(6);
                ps1.Cancel();
                ps2.ExpectNext(1, 2, 3, 4, 5, 6);
                await ps2.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
