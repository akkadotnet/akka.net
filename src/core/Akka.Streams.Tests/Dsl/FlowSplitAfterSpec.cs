//-----------------------------------------------------------------------
// <copyright file="FlowSplitAfterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod
// ReSharper disable UnusedMember.Local

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSplitAfterSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSplitAfterSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings =
                ActorMaterializerSettings.Create(Sys)
                    .WithInputBuffer(2, 2)
                    .WithSubscriptionTimeoutSettings(
                        new StreamSubscriptionTimeoutSettings(
                            StreamSubscriptionTimeoutTerminationMode.CancelTermination, TimeSpan.FromSeconds(1)));
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private sealed class StreamPuppet
        {
            private readonly TestSubscriber.ManualProbe<int> _probe;
            private readonly ISubscription _subscription;

            public StreamPuppet(IPublisher<int> p, TestKitBase kit)
            {
                _probe = kit.CreateManualSubscriberProbe<int>();
                p.Subscribe(_probe);
                _subscription = _probe.ExpectSubscription();
            }

            public void Request(int demand) => _subscription.Request(demand);

            public void ExpectNext(int element) => _probe.ExpectNext(element);
            public async Task ExpectNextAsync(int element) => await _probe.ExpectNextAsync(element);

            public void ExpectNoMsg(TimeSpan max) => _probe.ExpectNoMsg(max);

            public async Task ExpectNoMsgAsync(TimeSpan max) => await _probe.ExpectNoMsgAsync(max);

            public void ExpectComplete() => _probe.ExpectComplete();

            public async Task ExpectCompleteAsync() => await _probe.ExpectCompleteAsync();

            public void ExpectError(Exception ex) => _probe.ExpectError().Should().Be(ex);

            public void Cancel() => _subscription.Cancel();
        }

        private async Task WithSubstreamsSupportAsync(int splitAfter = 3, int elementCount = 6,
            SubstreamCancelStrategy substreamCancelStrategy = SubstreamCancelStrategy.Drain,
            Action<TestSubscriber.ManualProbe<Source<int, NotUsed>>, ISubscription, Func<Task<Source<int, NotUsed>>>> run = null)
        {

            var source = Source.From(Enumerable.Range(1, elementCount));
            var groupStream =
                source.SplitAfter(substreamCancelStrategy, i => i == splitAfter)
                    .Lift()
                    .RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);
            var masterSubscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = await masterSubscriber.ExpectSubscriptionAsync();

            run(masterSubscriber, masterSubscription, async() =>
            {
                masterSubscription.Request(1);
                return await masterSubscriber.ExpectNextAsync();
            });
        }

        [Fact]
        public async Task SplitAfter_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(async() => 
            {
                await WithSubstreamsSupportAsync(3, 5, 
                    run: async(masterSubscriber, masterSubscription, expectSubFlow) =>                                                                         
                    {
                        var flow = await expectSubFlow();
                        var s1 = new StreamPuppet(flow
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                             
                        await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                                                                             
                        s1.Request(2);                                                                             
                        await s1.ExpectNextAsync(1);                                                                             
                        await s1.ExpectNextAsync(2);                                                                             
                        s1.Request(1);                                                                             
                        await s1.ExpectNextAsync(3);                                                                            
                        s1.Request(1);                                                                             
                        await s1.ExpectCompleteAsync();

                        var flow2 = await expectSubFlow();
                        var s2 = new StreamPuppet(flow2
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                             
                        s2.Request(2);                                                                             
                        await s2.ExpectNextAsync(4);                                                                             
                        await s2.ExpectNextAsync(5);                                                                             
                        await s2.ExpectCompleteAsync();
                                                                             
                        masterSubscription.Request(1);                                                                             
                        await masterSubscriber.ExpectCompleteAsync();                                                                         
                    });
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_work_when_first_element_is_split_by()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await WithSubstreamsSupportAsync(1, 3, 
                    run: async(masterSubscriber, masterSubscription, expectSubFlow) =>                                                                         
                    {
                        var flow1 = await expectSubFlow();
                        var s1 = new StreamPuppet(flow1
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                             
                        await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));                                                                             
                        s1.Request(3);                                                                             
                        await s1.ExpectNextAsync(1);                                                                             
                        await s1.ExpectCompleteAsync();

                        var flow2 = await expectSubFlow();
                        var s2 = new StreamPuppet(flow2
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                             
                        s2.Request(3);                                                                             
                        await s2.ExpectNextAsync(2);                                                                             
                        await s2.ExpectNextAsync(3);                                                                             
                        await s2.ExpectCompleteAsync();                                                                             
                        masterSubscription.Request(1);                                                                             
                        await masterSubscriber.ExpectCompleteAsync();                                                                         
                    });
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_work_with_single_element_splits_by()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task = Source.From(Enumerable.Range(1, 10))                                                                             
                .SplitAfter(_ => true)                                                                             
                .Lift()                                                                             
                .SelectAsync(1, s => s.RunWith(Sink.First<int>(), Materializer))                                                                             
                .Grouped(10)                                                                             
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_support_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await WithSubstreamsSupportAsync(5, 8, 
                    run: async(masterSubscriber, masterSubscription, expectSubFlow) =>                                                                         
                    {
                        var flow1 = await expectSubFlow();
                        var s1 = new StreamPuppet(flow1.RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                            
                        masterSubscription.Cancel();                                                                             
                        s1.Request(5);                                                                             
                        await s1.ExpectNextAsync(1);                                                                             
                        await s1.ExpectNextAsync(2);                                                                             
                        await s1.ExpectNextAsync(3);                                                                             
                        await s1.ExpectNextAsync(4);                                                                             
                        await s1.ExpectNextAsync(5);                                                                             
                        s1.Request(1);                                                                             
                        await s1.ExpectCompleteAsync();                                                                         
                    });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_fail_stream_when_SplitAfter_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).SplitAfter(i =>
                {
                    if (i == 3)
                        throw ex;
                    return i % 3 == 0;
                }).Lift().RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);

                var subscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = await publisherProbe.ExpectSubscriptionAsync();
                var downstreamSubscription = await subscriber.ExpectSubscriptionAsync();

                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);

                var substream = await subscriber.ExpectNextAsync();
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(10);
                await substreamPuppet.ExpectNextAsync(1);

                upstreamSubscription.SendNext(2);
                await substreamPuppet.ExpectNextAsync(2);

                upstreamSubscription.SendNext(3);

                subscriber.ExpectError().Should().Be(ex);
                substreamPuppet.ExpectError(ex);
                await upstreamSubscription.ExpectCancellationAsync();
                //return Task.CompletedTask;
            }, Materializer);
        }

        [Fact(Skip = "Supervision is not supported fully by GraphStages yet")]
        public async Task SplitAfter_must_resume_stream_when_SplitAfter_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .SplitAfter(i => i % 3 == 0)
                        .Lift()
                        .To(Sink.FromSubscriber(down))
                        .Run(Materializer);
                var downstream = await down.ExpectSubscriptionAsync();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = await up.ExpectSubscriptionAsync();
                await upSub.ExpectCancellationAsync();
                //return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_must_support_eager_cancellation_of_master_stream_on_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                await WithSubstreamsSupportAsync(5, 8, SubstreamCancelStrategy.Propagate,                                                                             
                   async (masterSubscriber, masterSubscription, expectSubFlow) =>                                                                             
                    {
                        var flow1 = await expectSubFlow();
                        var s1 = new StreamPuppet(flow1.RunWith(Sink.AsPublisher<int>(false), Materializer),                                                                                     
                            this);                                                                                 
                        s1.Cancel();                                                                                 
                        await masterSubscriber.ExpectCompleteAsync();                                                                             
                    });
            }, Materializer);
        }

        [Fact]
        public async Task SplitAfter_should_work_when_last_element_is_split_by() => await this.AssertAllStagesStoppedAsync(async() => {
            await WithSubstreamsSupportAsync(splitAfter: 3, elementCount: 3,                                                                                                                                                 
                run: async(masterSubscriber, masterSubscription, expectSubFlow) =>                                                                                                                                                 
                {
                    var flow1 = await expectSubFlow();
                    var s1 = new StreamPuppet(flow1                                                                                                                                                         
                        .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                                                                                                     
                    await masterSubscriber.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));                                                                                                                                                     
                    s1.Request(3);                                                                                                                                                     
                    await s1.ExpectNextAsync(1);                                                                                                                                                     
                    await s1.ExpectNextAsync(2);                                                                                                                                                     
                    await s1.ExpectNextAsync(3);                                                                                                                                                     
                    await s1.ExpectCompleteAsync();                                                                                                                                                     
                    masterSubscription.Request(1);                                                                                                                                                     
                    await masterSubscriber.ExpectCompleteAsync();                                                                                                                                                 
                });
            return Task.CompletedTask;
        }, Materializer);

        [Fact]
        public async Task SplitAfter_should_fail_stream_if_substream_not_materialized_in_time() => await this.AssertAllStagesStoppedAsync(() => {
            var timeout = new StreamSubscriptionTimeoutSettings(StreamSubscriptionTimeoutTerminationMode.CancelTermination, TimeSpan.FromMilliseconds(500));
            var settings = ActorMaterializerSettings.Create(Sys).WithSubscriptionTimeoutSettings(timeout);
            var tightTimeoutMaterializer = ActorMaterializer.Create(Sys, settings);

            var testSource = Source.Single(1).ConcatMaterialized(Source.Maybe<int>(), Keep.Left).SplitAfter(_ => true);

            Func<Task> a = async () =>
            {
                await testSource.Lift().Delay(TimeSpan.FromSeconds(1)).ConcatMany(x => x)
                    .RunWith(Sink.Ignore<int>(), tightTimeoutMaterializer)
                    .WaitAsync(TimeSpan.FromSeconds(3));
            };
            a.Should().Throw<SubscriptionTimeoutException>();
            return Task.CompletedTask;
        }, Materializer);

        // Probably covert by SplitAfter_should_work_when_last_element_is_split_by
        // but we received a specific example which we want to cover too,
        // see https://github.com/akkadotnet/akka.net/issues/3222
        [Fact]
        public async Task SplitAfter_should_not_create_a_subflow_when_no_element_is_left()
        {
            var result = new ConcurrentQueue<ImmutableList<(bool, int)>>();
            Source.From(new[]
                {
                    (true, 1), (true, 2), (false, 0),
                    (true, 3), (true, 4), (false, 0),
                    (true, 5), (false, 0)
                })
                .SplitAfter(t => !t.Item1)
                .Where(t => t.Item1)
                .Aggregate(ImmutableList.Create<(bool, int)>(), (list, b) => list.Add(b))
                .To(Sink.ForEach<ImmutableList<(bool, int)>>(list => result.Enqueue(list)))
                .Run(Materializer);

            await Task.Delay(500);
            result.All(l => l.Count > 0).Should().BeTrue();
        }
    }
}
