//-----------------------------------------------------------------------
// <copyright file="FlowSplitAfterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
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

            public void ExpectNoMsg(TimeSpan max) => _probe.ExpectNoMsg(max);

            public void ExpectComplete() => _probe.ExpectComplete();

            public void ExpectError(Exception ex) => _probe.ExpectError().Should().Be(ex);

            public void Cancel() => _subscription.Cancel();
        }

        private void WithSubstreamsSupport(int splitAfter = 3, int elementCount = 6,
            SubstreamCancelStrategy substreamCancelStrategy = SubstreamCancelStrategy.Drain,
            Action<TestSubscriber.ManualProbe<Source<int, NotUsed>>, ISubscription, Func<Source<int, NotUsed>>> run = null)
        {

            var source = Source.From(Enumerable.Range(1, elementCount));
            var groupStream =
                source.SplitAfter(substreamCancelStrategy, i => i == splitAfter)
                    .Lift()
                    .RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);
            var masterSubscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = masterSubscriber.ExpectSubscription();

            run?.Invoke(masterSubscriber, masterSubscription, () =>
            {
                masterSubscription.Request(1);
                return masterSubscriber.ExpectNext();
            });
        }

        [Fact]
        public void SplitAfter_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(3,5,run: (masterSubscriber, masterSubscription, expectSubFlow) =>
                {
                    var s1 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.Request(2);
                    s1.ExpectNext(1);
                    s1.ExpectNext(2);
                    s1.Request(1);
                    s1.ExpectNext(3);
                    s1.Request(1);
                    s1.ExpectComplete();

                    var s2 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    s2.Request(2);
                    s2.ExpectNext(4);
                    s2.ExpectNext(5);
                    s2.ExpectComplete();

                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_work_when_first_element_is_split_by()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(1, 3, run: (masterSubscriber, masterSubscription, expectSubFlow) =>
                {
                    var s1 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.Request(3);
                    s1.ExpectNext(1);
                    s1.ExpectComplete();

                    var s2 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    s2.Request(3);
                    s2.ExpectNext(2);
                    s2.ExpectNext(3);
                    s2.ExpectComplete();

                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_work_with_single_element_splits_by()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 10))
                    .SplitAfter(_ => true)
                    .Lift()
                    .SelectAsync(1, s => s.RunWith(Sink.First<int>(), Materializer))
                    .Grouped(10)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
                task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_support_cancelling_substreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(5, 8, run: (masterSubscriber, masterSubscription, expectSubFlow) =>
                {
                    var s1 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    masterSubscription.Cancel();
                    s1.Request(5);
                    s1.ExpectNext(1);
                    s1.ExpectNext(2);
                    s1.ExpectNext(3);
                    s1.ExpectNext(4);
                    s1.ExpectNext(5);
                    s1.Request(1);
                    s1.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_fail_stream_when_SplitAfter_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = this.CreateManualPublisherProbe<int>();
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).SplitAfter(i =>
                {
                    if (i == 3)
                        throw ex;
                    return i%3 == 0;
                }).Lift().RunWith(Sink.AsPublisher<Source<int, NotUsed>>(false), Materializer);
                
                var subscriber = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();

                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);

                var substream = subscriber.ExpectNext();
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(10);
                substreamPuppet.ExpectNext(1);

                upstreamSubscription.SendNext(2);
                substreamPuppet.ExpectNext(2);

                upstreamSubscription.SendNext(3);

                subscriber.ExpectError().Should().Be(ex);
                substreamPuppet.ExpectError(ex);
                upstreamSubscription.ExpectCancellation();
            }, Materializer);
        }

        [Fact(Skip = "Supervision is not supported fully by GraphStages yet")]
        public void SplitAfter_must_resume_stream_when_SplitAfter_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {

            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = this.CreateManualPublisherProbe<int>();
                var down = this.CreateManualSubscriberProbe<Source<int, NotUsed>>();

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .SplitAfter(i => i%3 == 0)
                        .Lift()
                        .To(Sink.FromSubscriber(down))
                        .Run(Materializer);
                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = up.ExpectSubscription();
                upSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void SplitAfter_must_support_eager_cancellation_of_master_stream_on_cancelling_substreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(5,8,SubstreamCancelStrategy.Propagate,
                    (masterSubscriber, masterSubscription, expectSubFlow) =>
                    {
                        var s1 = new StreamPuppet(expectSubFlow().RunWith(Sink.AsPublisher<int>(false), Materializer),
                            this);
                        s1.Cancel();
                        masterSubscriber.ExpectComplete();
                    });
            }, Materializer);
        }
    }
}
