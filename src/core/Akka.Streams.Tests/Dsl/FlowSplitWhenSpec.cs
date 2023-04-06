//-----------------------------------------------------------------------
// <copyright file="FlowSplitWhenSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Reactive.Streams;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowSplitWhenSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowSplitWhenSpec(ITestOutputHelper helper) : base(helper)
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

            public async Task ExpectNextAsync(int element) => await _probe.ExpectNextAsync(element);

            public void ExpectNext(int element) => _probe.ExpectNext(element);

            public void ExpectNoMsg(TimeSpan max) => _probe.ExpectNoMsg(max);

            public async Task ExpectCompleteAsync() => await _probe.ExpectCompleteAsync();

            public void ExpectComplete() => _probe.ExpectComplete();

            public void ExpectError(Exception ex) => _probe.ExpectError().Should().Be(ex);

            public void Cancel() => _subscription.Cancel();
        }
       
        private void WithSubstreamsSupport(int splitWhen = 3, int elementCount = 6,
            SubstreamCancelStrategy substreamCancelStrategy = SubstreamCancelStrategy.Drain,
            Action<TestSubscriber.ManualProbe<Source<int, NotUsed>>, ISubscription, Func<Source<int, NotUsed>>> run = null)
        {

            var source = Source.From(Enumerable.Range(1, elementCount));
            var groupStream =
                source.SplitWhen(substreamCancelStrategy, i => i == splitWhen)
                    .Lift()
                    .RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);
            var masterSubscriber = TestSubscriber.CreateManualSubscriberProbe<Source<int, NotUsed>>(this);
            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = masterSubscriber.ExpectSubscription();

            run?.Invoke(masterSubscriber, masterSubscription, () =>
            {
                masterSubscription.Request(1);
                return masterSubscriber.ExpectNext();
            });
        }

        [Fact]
        public async Task SplitWhen_must_work_in_the_happy_case()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                WithSubstreamsSupport(elementCount: 4,                                                                             
                    run: (masterSubscriber, masterSubscription, getSubFlow) =>                                                                             
                    {
                        var s1 = new StreamPuppet(getSubFlow()                                                                                     
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                                 
                        masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(4100));
                        s1.Request(2);                                                                                 
                        s1.ExpectNext(1);
                        s1.ExpectNext(2);
                        s1.Request(1);                                                                                 
                        s1.ExpectComplete();

                        
                        var s2 = new StreamPuppet(getSubFlow()                                                                                     
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);                                                                                 
                        masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                        s2.Request(1);                                                                                 
                        s2.ExpectNext(3);
                        s2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                        s2.Request(1);                                                                                 
                        s2.ExpectNext(4);
                        s2.Request(1);                                                                                 
                        s2.ExpectComplete();
                                                                                 
                        masterSubscription.Request(1);                                                                                 
                        masterSubscriber.ExpectComplete();                                                                             
                    });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_not_emit_substreams_if_the_parent_stream_is_empty()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task =                                                                             
                Source.Empty<int>()                                                                                 
                .SplitWhen(_ => true)                                                                                 
                .Lift()                                                                                 
                .SelectAsync(1, s => s.RunWith(Sink.FirstOrDefault<int>(), Materializer))                                                                                 
                .Grouped(10)                                                                                 
                .RunWith(Sink.FirstOrDefault<IEnumerable<int>>(),                                                                             
                Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(default(IEnumerable<int>));
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_work_when_first_element_is_split_by()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                WithSubstreamsSupport(1, 3,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet(getSubFlow()
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                        s1.Request(5);
                        await s1.ExpectNextAsync(1);
                        await s1.ExpectNextAsync(2);
                        await s1.ExpectNextAsync(3);
                        await s1.ExpectCompleteAsync();

                        masterSubscription.Request(1);
                        await masterSubscriber.ExpectCompleteAsync();
                    });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                WithSubstreamsSupport(5, 8,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet(getSubFlow()
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        s1.Cancel();
                        var s2 = new StreamPuppet(getSubFlow()
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                        s2.Request(4);
                        await s2.ExpectNextAsync(5);
                        await s2.ExpectNextAsync(6);
                        await s2.ExpectNextAsync(7);
                        await s2.ExpectNextAsync(8);
                        s2.Request(1);
                        await s2.ExpectCompleteAsync();

                        masterSubscription.Request(1);
                        await masterSubscriber.ExpectCompleteAsync();
                    });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_both_master_and_substream()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var inputs = this.CreatePublisherProbe<int>();

                var substream = this.CreateSubscriberProbe<int>();
                var masterStream = this.CreateSubscriberProbe<NotUsed>();

                Source.FromPublisher(inputs)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .Select(x => x.RunWith(Sink.FromSubscriber(substream), Materializer))
                    .RunWith(Sink.FromSubscriber(masterStream), Materializer);

                masterStream.Request(1);
                await inputs.SendNextAsync(1);

                substream.Cancel();

                await masterStream.ExpectNextAsync(NotUsed.Instance);
                await masterStream.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                masterStream.Cancel();
                await inputs.ExpectCancellationAsync();

                var inputs2 = this.CreatePublisherProbe<int>();
                Source.FromPublisher(inputs2)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .Select(x => x.RunWith(Sink.Cancelled<int>(), Materializer))
                    .RunWith(Sink.Cancelled<NotUsed>(), Materializer);
                await inputs2.ExpectCancellationAsync();

                var inputs3 = this.CreatePublisherProbe<int>();
                var masterStream3 = this.CreateSubscriberProbe<Source<int, NotUsed>>();

                Source.FromPublisher(inputs3)
                    .SplitWhen(x => x == 2)
                    .Lift()
                    .RunWith(Sink.FromSubscriber(masterStream3), Materializer);

                masterStream3.Request(1);
                await inputs3.SendNextAsync(1);

                var src = await masterStream3.ExpectNextAsync();
                src.RunWith(Sink.Cancelled<int>(), Materializer);

                masterStream3.Request(1);
                await inputs3.SendNextAsync(2);
                var src2 = await masterStream3.ExpectNextAsync();
                var substream4 = this.CreateSubscriberProbe<int>();
                src2.RunWith(Sink.FromSubscriber(substream4), Materializer);

                await substream4.RequestNextAsync(2);
                await substream4.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await masterStream3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await inputs3.ExpectRequestAsync();
                await inputs3.ExpectRequestAsync();
                await inputs3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                substream4.Cancel();
                await inputs3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));
                await masterStream3.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(100));

                masterStream3.Cancel();
                await inputs3.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_support_cancelling_the_master_stream()
        {
            await this.AssertAllStagesStoppedAsync(() =>
            {
                WithSubstreamsSupport(5, 8,
                    run: async (masterSubscriber, masterSubscription, getSubFlow) =>
                    {
                        var s1 = new StreamPuppet(getSubFlow()
                            .RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                        masterSubscription.Cancel();

                        s1.Request(4);
                        await s1.ExpectNextAsync(1);
                        await s1.ExpectNextAsync(2);
                        await s1.ExpectNextAsync(3);
                        await s1.ExpectNextAsync(4);
                        s1.Request(1);
                        await s1.ExpectCompleteAsync();
                    });
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_fail_stream_when_SplitWhen_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).SplitWhen(i =>
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
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_work_with_single_element_splits()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task = Source.From(Enumerable.Range(1, 100))                                                                             
                .SplitWhen(_ => true)                                                                             
                .Lift()                                                                             
                .SelectAsync(1, s => s.RunWith(Sink.First<int>(), Materializer)) // Please note that this line *also* implicitly asserts nonempty substreams                                                                             
                .Grouped(200)                                                                             
                .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
                return Task.CompletedTask;
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task SplitWhen_must_fail_substream_if_materialized_twice()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var task = Source.Single(1).SplitWhen(_ => true).Lift()                                                                             
                .SelectAsync(1, source =>                                                                             
                {                                                                                 
                    source.RunWith(Sink.Ignore<int>(), Materializer);                                                                                 
                    // Sink.ignore+mapAsync pipes error back                                                                                 
                    return Task.Run(() =>                                                                                 
                    {                                                                                     
                        source.RunWith(Sink.Ignore<int>(), Materializer).Wait(TimeSpan.FromSeconds(3));                                                                                     
                        return 1;                                                                                 
                    });                                                                             
                })                                                                             
                .RunWith(Sink.Ignore<int>(), Materializer);
                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .Should().Throw<IllegalStateException>();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_fail_stream_if_substream_not_materialized_in_time()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var tightTimeoutMaterializer = ActorMaterializer.Create(Sys,                                                                             
                    ActorMaterializerSettings.Create(Sys)                                                                                 
                    .WithSubscriptionTimeoutSettings(                                                                                     
                        new StreamSubscriptionTimeoutSettings(                                                                                         
                            StreamSubscriptionTimeoutTerminationMode.CancelTermination,                                                                                         
                            TimeSpan.FromMilliseconds(500))));
                var testSource =
                    Source.Single(1)
                        .MapMaterializedValue<TaskCompletionSource<int>>(_ => null)
                        .Concat(Source.Maybe<int>())
                        .SplitWhen(_ => true);
                Action action = () =>
                {
                    var task =
                        testSource.Lift()
                            .Delay(TimeSpan.FromSeconds(1))
                            .ConcatMany(s => s.MapMaterializedValue<TaskCompletionSource<int>>(_ => null))
                            .RunWith(Sink.Ignore<int>(), tightTimeoutMaterializer);
                    task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                };

                action.Should().Throw<SubscriptionTimeoutException>();
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact(Skip = "Supervision is not supported fully by GraphStages yet")]
        public async Task SplitWhen_must_resume_stream_when_splitWhen_function_throws()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                return Task.CompletedTask;
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_pass_along_early_cancellation()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .SplitWhen(i => i % 3 == 0)
                        .Lift()
                        .To(Sink.FromSubscriber(down))
                        .Run(Materializer);
                var downstream = await down.ExpectSubscriptionAsync();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = await up.ExpectSubscriptionAsync();
                await upSub.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task SplitWhen_must_support_eager_cancellation_of_master_stream_on_cancelling_substreams()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                WithSubstreamsSupport(5, 8, SubstreamCancelStrategy.Propagate,                                                                             
                    async (masterSubscriber, masterSubscription, expectSubFlow) =>                                                                             
                    {                                                                                 
                        var s1 = new StreamPuppet(expectSubFlow()                                                                                     
                            .RunWith(Sink.AsPublisher<int>(false), Materializer),                                                                                     
                            this);                                                                                 
                        s1.Cancel();                                                                                 
                        await masterSubscriber.ExpectCompleteAsync();
                                                                             
                    });
                return Task.CompletedTask;
            }, Materializer);
        }
    }
}
