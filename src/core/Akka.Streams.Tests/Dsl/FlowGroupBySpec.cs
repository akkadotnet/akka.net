//-----------------------------------------------------------------------
// <copyright file="FlowGroupBySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Dsl.Internal;
using Akka.Streams.Supervision;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowGroupBySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowGroupBySpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);                
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private sealed class StreamPuppet
        {
            private readonly TestSubscriber.ManualProbe<int> _probe;
            private readonly ISubscription _subscription;

            public StreamPuppet(IPublisher<int> p, TestKitBase kit)
            {
                _probe = TestSubscriber.CreateManualProbe<int>(kit);
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

        private void WithSubstreamsSupport(int groupCount = 2, int elementCount = 6, int maxSubstream = -1,
            Action<TestSubscriber.ManualProbe<Tuple<int, Source<int, NotUsed>>>, ISubscription, Func<int, Source<int, NotUsed>>> run = null)
        {

            var source = Source.From(Enumerable.Range(1, elementCount)).RunWith(Sink.AsPublisher<int>(false), Materializer);
            var max = maxSubstream > 0 ? maxSubstream : groupCount;
            var groupStream =
                Source.FromPublisher(source)
                    .GroupBy(max, x => x%groupCount)
                    .Lift(x => x%groupCount)
                    .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);
            var masterSubscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);

            groupStream.Subscribe(masterSubscriber);
            var masterSubscription = masterSubscriber.ExpectSubscription();

            run?.Invoke(masterSubscriber, masterSubscription, expectedKey =>
            {
                masterSubscription.Request(1);
                var tuple =  masterSubscriber.ExpectNext();
                tuple.Item1.Should().Be(expectedKey);
                return tuple.Item2;
            });
        }

        [Fact]
        public void GroupBy_must_work_in_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(2, run: (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    var s1 = new StreamPuppet(getSubFlow(1).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    masterSubscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                    s1.Request(1);
                    s1.ExpectNext(1);
                    s1.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    var s2 = new StreamPuppet(getSubFlow(0).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    s2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                    s2.Request(2);
                    s2.ExpectNext(2);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fanout box
                    s1.Request(1);
                    s2.ExpectNext(4);

                    s2.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    s1.ExpectNext(3);

                    s2.Request(1);
                    // Important to request here on the OTHER stream because the buffer space is exactly one without the fanout box
                    s1.Request(1);
                    s2.ExpectNext(6);
                    s2.ExpectComplete();

                    s1.ExpectNext(5);
                    s1.ExpectComplete();
                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_in_normal_user_scenario()
        {
            var sub = (SubFlow<IEnumerable<string>, NotUsed, IRunnableGraph<NotUsed>>) Source.From(new[] {"Aaa", "Abb", "Bcc", "Cdd", "Cee"})
                .GroupBy(3, s => s.Substring(0, 1))
                .Grouped(10);
            var source = (Source<IEnumerable<string>, NotUsed>)sub.MergeSubstreams();
            var task  = source.Grouped(10).RunWith(Sink.First<IEnumerable<IEnumerable<string>>>(), Materializer);
            task.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            task.Result.OrderBy(e => e.First())
                .ShouldBeEquivalentTo(new[] {new[] {"Aaa", "Abb"}, new[] {"Bcc"}, new[] {"Cdd", "Cee"}});
        }

        [Fact]
        public void GroupBy_must_support_cancelling_substreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                WithSubstreamsSupport(2, run: (masterSubscriber, masterSubscription, getSubFlow) =>
                {
                    new StreamPuppet(getSubFlow(1).RunWith(Sink.AsPublisher<int>(false), Materializer), this).Cancel();
                    var substream = new StreamPuppet(getSubFlow(0).RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                    
                    substream.Request(2);
                    substream.ExpectNext(2);
                    substream.ExpectNext(4);
                    substream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));

                    substream.Request(2);
                    substream.ExpectNext(6);
                    substream.ExpectComplete();

                    masterSubscription.Request(1);
                    masterSubscriber.ExpectComplete();
                });
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_accept_cancellation_of_master_stream_when_not_consume_anything()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = TestPublisher.CreateManualProbe<int>(this);
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x%2)
                        .Lift(x => x%2)
                        .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Cancel();
                upstreamSubscription.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_work_with_empty_input_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher =
                    Source.From(new List<int>())
                        .GroupBy(2, x => x%2)
                        .Lift(x => x%2)
                        .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                subscriber.ExpectSubscriptionAndComplete();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_abort_onError_from_upstream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = TestPublisher.CreateManualProbe<int>(this);
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);
                subscriber.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_abort_onError_from_upstream_when_substreams_are_running()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = TestPublisher.CreateManualProbe<int>(this);
                var publisher =
                    Source.FromPublisher(publisherProbe)
                        .GroupBy(2, x => x % 2)
                        .Lift(x => x % 2)
                        .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);
                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);
                upstreamSubscription.SendNext(1);
                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(1);
                substreamPuppet.ExpectNext(1);

                var ex = new TestException("test");
                upstreamSubscription.SendError(ex);

                substreamPuppet.ExpectError(ex);
                subscriber.ExpectError().Should().Be(ex);
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_fail_stream_when_GroupBy_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = TestPublisher.CreateManualProbe<int>(this);
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i =>
                {
                    if (i == 2)
                        throw ex;
                    return i%2;
                })
                    .Lift(x => x%2)
                    .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);


                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet.Request(1);
                substreamPuppet.ExpectNext(1);

                upstreamSubscription.SendNext(2);
                subscriber.ExpectError().Should().Be(ex);
                substreamPuppet.ExpectError(ex);
                upstreamSubscription.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_resume_stream_when_GroupBy_function_throws()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisherProbe = TestPublisher.CreateManualProbe<int>(this);
                var ex = new TestException("test");
                var publisher = Source.FromPublisher(publisherProbe).GroupBy(2, i =>
                {
                    if (i == 2)
                        throw ex;
                    return i%2;
                })
                    .Lift(x => x%2)
                    .WithAttributes(ActorAttributes.CreateSupervisionStrategy(Deciders.ResumingDecider))
                    .RunWith(Sink.AsPublisher<Tuple<int, Source<int, NotUsed>>>(false), Materializer);

                var subscriber = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);
                publisher.Subscribe(subscriber);

                var upstreamSubscription = publisherProbe.ExpectSubscription();
                var downstreamSubscription = subscriber.ExpectSubscription();
                downstreamSubscription.Request(100);

                upstreamSubscription.SendNext(1);

                var substream = subscriber.ExpectNext().Item2;
                var substreamPuppet1 = new StreamPuppet(substream.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                substreamPuppet1.Request(10);
                substreamPuppet1.ExpectNext(1);

                upstreamSubscription.SendNext(2);
                upstreamSubscription.SendNext(4);

                var substream2 = subscriber.ExpectNext().Item2;
                var substreamPuppet2 = new StreamPuppet(substream2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);
                substreamPuppet2.Request(10);
                substreamPuppet2.ExpectNext(4);

                upstreamSubscription.SendNext(3);
                substreamPuppet1.ExpectNext(3);

                upstreamSubscription.SendNext(6);
                substreamPuppet2.ExpectNext(6);

                upstreamSubscription.SendComplete();
                subscriber.ExpectComplete();
                substreamPuppet1.ExpectComplete();
                substreamPuppet2.ExpectComplete();

            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_pass_along_early_cancellation()
        {
            this.AssertAllStagesStopped(() =>
            {
                var up = TestPublisher.CreateManualProbe<int>(this);
                var down = TestSubscriber.CreateManualProbe<Tuple<int, Source<int, NotUsed>>>(this);

                var flowSubscriber =
                    Source.AsSubscriber<int>()
                        .GroupBy(2, x => x%2)
                        .Lift(x => x%2)
                        .To(Sink.FromSubscriber(down)).Run(Materializer);
                
                var downstream = down.ExpectSubscription();
                downstream.Cancel();
                up.Subscribe(flowSubscriber);
                var upSub = up.ExpectSubscription();
                upSub.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void GroupBy_must_fail_when_exceeding_maxSubstreams()
        {
            this.AssertAllStagesStopped(() =>
            {
                var sub = Flow.Create<int>().GroupBy(1, x => x%2).PrefixAndTail(0);
                var f = ((SubFlow<Tuple<IImmutableList<int>, Source<int, NotUsed>>, NotUsed, Sink<int, NotUsed>>) sub).MergeSubstreams();
                var t = ((Flow<int, Tuple<IImmutableList<int>, Source<int, NotUsed>>, NotUsed>) f)
                    .RunWith(TestSource.SourceProbe<int>(this), TestSink.SinkProbe<Tuple<IImmutableList<int>, Source<int, NotUsed>>>(this), Materializer);
                var up = t.Item1;
                var down = t.Item2;

                down.Request(2);

                up.SendNext(1);
                var first = down.ExpectNext();
                var s1 = new StreamPuppet(first.Item2.RunWith(Sink.AsPublisher<int>(false), Materializer), this);

                s1.Request(1);
                s1.ExpectNext(1);

                up.SendNext(2);
                var ex = down.ExpectError();
                ex.Message.Should().Contain("too many substreams");
                s1.ExpectError(ex);
            }, Materializer);
        }
    }
}
