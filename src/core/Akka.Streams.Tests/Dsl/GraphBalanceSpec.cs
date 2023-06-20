//-----------------------------------------------------------------------
// <copyright file="GraphBalanceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphBalanceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphBalanceSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 16);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_Balance_must_balance_between_subscribers_which_signal_demand()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var balance = b.Add(new Balance<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(balance.Out(1)).To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(1);
                await c1.ExpectNext(1).ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                sub2.Request(2);
                c2.ExpectNext(2, 3);
                await c1.ExpectCompleteAsync();
                await c2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_support_waiting_for_demand_from_all_downstream_subscriptions()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s1 = this.CreateManualSubscriberProbe<int>();

                var p2 = RunnableGraph.FromGraph(GraphDsl.Create(Sink.AsPublisher<int>(false), (b, p2Sink) =>
                {
                    var balance = b.Add(new Balance<int>(2, true));
                    var source = Source.From(Enumerable.Range(1, 3)).MapMaterializedValue<IPublisher<int>>(_ => null);
                    b.From(source).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(s1).MapMaterializedValue<IPublisher<int>>(_ => null));
                    b.From(balance.Out(1)).To(p2Sink);
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var sub1 = await s1.ExpectSubscriptionAsync();

                sub1.Request(1);
                await s1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                var s2 = this.CreateManualSubscriberProbe<int>();
                p2.Subscribe(s2);
                var sub2 = await s2.ExpectSubscriptionAsync();

                // still no demand from s2
                await s2.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));

                sub2.Request(2);
                await s1.ExpectNextAsync(1);
                s2.ExpectNext(2, 3);
                await s1.ExpectCompleteAsync();
                await s2.ExpectCompleteAsync();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task A_Balance_must_support_waiting_for_demand_from_all_non_cancelled_downstream_subscriptions()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var s1 = this.CreateManualSubscriberProbe<int>();

                var t = RunnableGraph.FromGraph(GraphDsl.Create(Sink.AsPublisher<int>(false),
                    Sink.AsPublisher<int>(false), Keep.Both, (b, p2Sink, p3Sink) =>
                    {
                        var balance = b.Add(new Balance<int>(3, true));
                        var source =
                            Source.From(Enumerable.Range(1, 3))
                                  .MapMaterializedValue(_ => default((IPublisher<int>, IPublisher<int>)));
                        b.From(source).To(balance.In);
                        b.From(balance.Out(0))
                            .To(
                                Sink.FromSubscriber(s1)
                                    .MapMaterializedValue(_ => default((IPublisher<int>, IPublisher<int>))));
                        b.From(balance.Out(1)).To(p2Sink);
                        b.From(balance.Out(2)).To(p3Sink);
                        return ClosedShape.Instance;
                    })).Run(Materializer);
                var p2 = t.Item1;
                var p3 = t.Item2;

                var sub1 = await s1.ExpectSubscriptionAsync();
                sub1.Request(1);

                var s2 = this.CreateManualSubscriberProbe<int>();
                p2.Subscribe(s2);
                var sub2 = await s2.ExpectSubscriptionAsync();

                var s3 = this.CreateManualSubscriberProbe<int>();
                p3.Subscribe(s3);
                var sub3 = await s3.ExpectSubscriptionAsync();

                sub2.Request(2);
                await s1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub3.Cancel();

                s1.ExpectNext(1);
                s2.ExpectNext(2, 3);
                await s1.ExpectCompleteAsync();
                await s2.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_work_with_1_way_balance()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task = Source.FromGraph(GraphDsl.Create(b =>                                                                         
                {                                                                             
                    var balance = b.Add(new Balance<int>(1));                                                                             
                    var source = b.Add(Source.From(Enumerable.Range(1, 3)));
                                                                             
                    b.From(source).To(balance.In);                                                                             
                    return new SourceShape<int>(balance.Out(0));                                                                         
                })).RunAggregate(new List<int>(), (list, i) =>                                                                         
                {                                                                             
                    list.Add(i);                                                                             
                    return list;                                                                         
                }, Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(new[] { 1, 2, 3 });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_work_with_5_way_balance()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var sink = Sink.First<IEnumerable<int>>();
                var t = RunnableGraph.FromGraph(GraphDsl.Create(sink, sink, sink, sink, sink, ValueTuple.Create,
                    (b, s1, s2, s3, s4, s5) =>
                    {
                        var balance = b.Add(new Balance<int>(5, true));
                        var source = Source.From(Enumerable.Range(0, 15)).MapMaterializedValue(_ => default((Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>, Task<IEnumerable<int>>)));
                        b.From(source).To(balance.In);
                        b.From(balance.Out(0)).Via(Flow.Create<int>().Grouped(15)).To(s1);
                        b.From(balance.Out(1)).Via(Flow.Create<int>().Grouped(15)).To(s2);
                        b.From(balance.Out(2)).Via(Flow.Create<int>().Grouped(15)).To(s3);
                        b.From(balance.Out(3)).Via(Flow.Create<int>().Grouped(15)).To(s4);
                        b.From(balance.Out(4)).Via(Flow.Create<int>().Grouped(15)).To(s5);
                        return ClosedShape.Instance;
                    })).Run(Materializer);

                var task = Task.WhenAll(t.Item1, t.Item2, t.Item3, t.Item4, t.Item5);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.SelectMany(l => l).Should().BeEquivalentTo(Enumerable.Range(0, 15));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_balance_between_all_three_outputs()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                const int numElementsForSink = 10000;
                var outputs = Sink.Aggregate<int, int>(0, (sum, i) => sum + i);
                var t = RunnableGraph.FromGraph(GraphDsl.Create(outputs, outputs, outputs, ValueTuple.Create,
                    (b, o1, o2, o3) =>
                    {
                        var balance = b.Add(new Balance<int>(3, true));
                        var source =
                            Source.Repeat(1)
                                .Take(numElementsForSink * 3)
                                .MapMaterializedValue(_ => default((Task<int>, Task<int>, Task<int>)));
                        b.From(source).To(balance.In);
                        b.From(balance.Out(0)).To(o1);
                        b.From(balance.Out(1)).To(o2);
                        b.From(balance.Out(2)).To(o3);
                        return ClosedShape.Instance;
                    })).Run(Materializer);

                var task = Task.WhenAll(t.Item1, t.Item2, t.Item3);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().NotContain(0);
                task.Result.Sum().Should().Be(numElementsForSink * 3);
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_fairly_balance_between_three_outputs()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var probe = this.SinkProbe<int>();
                var t = RunnableGraph.FromGraph(GraphDsl.Create(probe, probe, probe, ValueTuple.Create,
                    (b, o1, o2, o3) =>
                    {
                        var balance = b.Add(new Balance<int>(3));
                        var source =
                            Source.From(Enumerable.Range(1, 7))
                                .MapMaterializedValue(_ => default((TestSubscriber.Probe<int>, TestSubscriber.Probe<int>, TestSubscriber.Probe<int>)));
                        b.From(source).To(balance.In);
                        b.From(balance.Out(0)).To(o1);
                        b.From(balance.Out(1)).To(o2);
                        b.From(balance.Out(2)).To(o3);
                        return ClosedShape.Instance;
                    })).Run(Materializer);
                var p1 = t.Item1;
                var p2 = t.Item2;
                var p3 = t.Item3;

                await p1.RequestNextAsync(1);
                await p2.RequestNextAsync(2);
                await p3.RequestNextAsync(3);
                await p2.RequestNextAsync(4);
                await p1.RequestNextAsync(5);
                await p3.RequestNextAsync(6);
                await p1.RequestNextAsync(7);

                await p1.ExpectCompleteAsync();
                await p2.ExpectCompleteAsync();
                await p3.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_produce_to_second_even_though_first_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var balance = b.Add(new Balance<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(balance.Out(1)).To(Sink.FromSubscriber(c2));
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
        public async Task A_Balance_must_produce_to_first_even_though_second_cancels()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var balance = b.Add(new Balance<int>(2));
                    var source = Source.From(Enumerable.Range(1, 3));
                    b.From(source).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(balance.Out(1)).To(Sink.FromSubscriber(c2));
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
        public async Task A_Balance_must_cancel_upstream_when_downstream_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p1 = this.CreateManualPublisherProbe<int>();
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var balance = b.Add(new Balance<int>(2));
                    var source = Source.FromPublisher(p1.Publisher);
                    b.From(source).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(balance.Out(1)).To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var bsub = await p1.ExpectSubscriptionAsync();
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(1);
                await p1.ExpectRequestAsync(bsub, 16);
                bsub.SendNext(1);
                await c1.ExpectNextAsync(1);

                sub2.Request(1);
                bsub.SendNext(2);
                await c2.ExpectNextAsync(2);

                sub1.Cancel();
                sub2.Cancel();
                await bsub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Balance_must_not_push_output_twice()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var p1 = this.CreateManualPublisherProbe<int>();
                var c1 = this.CreateManualSubscriberProbe<int>();
                var c2 = this.CreateManualSubscriberProbe<int>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var balance = b.Add(new Balance<int>(2));
                    b.From(Source.FromPublisher(p1.Publisher)).To(balance.In);
                    b.From(balance.Out(0)).To(Sink.FromSubscriber(c1));
                    b.From(balance.Out(1)).To(Sink.FromSubscriber(c2));
                    return ClosedShape.Instance;
                })).Run(Materializer);

                var bsub = await p1.ExpectSubscriptionAsync();
                var sub1 = await c1.ExpectSubscriptionAsync();
                var sub2 = await c2.ExpectSubscriptionAsync();

                sub1.Request(1);
                await p1.ExpectRequestAsync(bsub, 16);
                bsub.SendNext(1);
                await c1.ExpectNextAsync(1);

                sub2.Request(1);
                sub2.Cancel();
                bsub.SendNext(2);

                sub1.Cancel();
                await bsub.ExpectCancellationAsync();
            }, Materializer);
        }
    }
}
