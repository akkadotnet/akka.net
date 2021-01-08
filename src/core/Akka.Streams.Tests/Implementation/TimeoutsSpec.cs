//-----------------------------------------------------------------------
// <copyright file="TimeoutsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Implementation
{
    public class TimeoutsSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public TimeoutsSpec(ITestOutputHelper helper = null) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void InitialTimeout_must_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .InitialTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }
        
        [Fact]
        public void InitialTimeout_must_pass_through_error_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .InitialTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>().WithMessage("test");

            }, Materializer);
        }

        [Fact]
        public void InitialTimeout_must_fail_if_no_initial_element_passes_until_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstreamProbe = this.CreateSubscriberProbe<int>();
                Source.Maybe<int>()
                .InitialTimeout(TimeSpan.FromSeconds(1))
                .RunWith(Sink.FromSubscriber(downstreamProbe), Materializer);

                downstreamProbe.ExpectSubscription();
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be($"The first element has not yet passed through in {TimeSpan.FromSeconds(1)}.");
            }, Materializer);
        }


        [Fact]
        public void CompletionTimeout_must_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .CompletionTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void CompletionTimeout_must_pass_through_error_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .CompletionTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>().WithMessage("test");
            }, Materializer);
        }
        
        [Fact]
        public void CompletionTimeout_must_fail_if_not_completed_until_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamProbe = this.CreatePublisherProbe<int>();
                var downstreamProbe = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstreamProbe)
                    .CompletionTimeout(TimeSpan.FromSeconds(2))
                    .RunWith(Sink.FromSubscriber(downstreamProbe), Materializer);


                upstreamProbe.SendNext(1);
                downstreamProbe.RequestNext(1);
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet

                upstreamProbe.SendNext(2);
                downstreamProbe.RequestNext(2);
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet

                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be($"The stream has not been completed in {TimeSpan.FromSeconds(2)}.");
            }, Materializer);
        }


        [Fact]
        public void IdleTimeout_must_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .IdleTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void IdleTimeout_must_pass_through_error_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .IdleTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Invoking(t => t.Wait(TimeSpan.FromSeconds(3)))
                    .ShouldThrow<TestException>().WithMessage("test");
            }, Materializer);
        }

        [Fact(Skip = "Racy")]
        public void IdleTimeout_must_fail_if_time_between_elements_is_too_large()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamProbe = this.CreatePublisherProbe<int>();
                var downstreamProbe = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstreamProbe)
                    .IdleTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(downstreamProbe), Materializer);

                // Two seconds in overall, but won't timeout until time between elements is large enough
                // (i.e. this works differently from completionTimeout)
                for (var i = 1; i <= 4; i++)
                {
                    upstreamProbe.SendNext(1);
                    downstreamProbe.RequestNext(1);
                    downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet   
                }
                
                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be($"No elements passed in the last {TimeSpan.FromSeconds(1)}.");
            }, Materializer);
        }
        

        [Fact]
        public void BackpressureTimeout_must_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 100))
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer)
                    .AwaitResult()
                    .ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void BackpressureTimeout_must_succeed_if_subscriber_demand_arrives()
        {
            this.AssertAllStagesStopped(() =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.From(new[] { 1, 2, 3, 4 })
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                for (var i = 1; i < 4; i++)
                {
                    subscriber.RequestNext(i);
                    subscriber.ExpectNoMsg(TimeSpan.FromMilliseconds(250));
                }

                subscriber.RequestNext(4);
                subscriber.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void BackpressureTimeout_must_not_throw_if_publisher_is_less_frequent_than_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<string>();
                var subscriber = this.CreateSubscriberProbe<string>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                subscriber.Request(2).ExpectNoMsg(TimeSpan.FromSeconds(1));

                publisher.SendNext("Quick Msg");
                subscriber.ExpectNext("Quick Msg");

                subscriber.ExpectNoMsg(TimeSpan.FromSeconds(3));
                publisher.SendNext("Slow Msg");
                subscriber.ExpectNext("Slow Msg");

                publisher.SendComplete();
                subscriber.ExpectComplete();
            }, Materializer);
        }
        
        [Fact]
        public void BackpressureTimeout_must_not_throw_if_publisher_wont_perform_emission_ever()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<string>();
                var subscriber = this.CreateSubscriberProbe<string>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                subscriber.Request(16).ExpectNoMsg(TimeSpan.FromSeconds(2));

                publisher.SendComplete();
                subscriber.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void BackpressureTimeout_must_throw_if_subscriber_wont_generate_demand_on_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                subscriber.Request(1);
                publisher.SendNext(1);
                subscriber.ExpectNext(1);

                Thread.Sleep(3000);

                subscriber.ExpectError().Message.Should().Be("No demand signalled in the last 00:00:01.");
            }, Materializer);
        }
        
        [Fact]
        public void BackpressureTimeout_must_throw_if_subscriber_never_generate_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                subscriber.ExpectSubscription();

                Thread.Sleep(3000);

                subscriber.ExpectError().Message.Should().Be("No demand signalled in the last 00:00:01.");
            }, Materializer);
        }
        
        [Fact]
        public void BackpressureTimeout_must_not_throw_if_publisher_completes_without_fulfilling_subscribers_demand()
        {
            this.AssertAllStagesStopped(() =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                subscriber.Request(2);
                publisher.SendNext(1);
                subscriber.ExpectNext(1);

                subscriber.ExpectNoMsg(TimeSpan.FromSeconds(2));

                publisher.SendComplete();
                subscriber.ExpectComplete();
            }, Materializer);
        }


        [Fact()]
        public void IdleTimeoutBidi_must_not_signal_error_in_simple_loopback_case_and_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var timeoutIdentity = BidiFlow.BidirectionalIdleTimeout<int, int>(TimeSpan.FromSeconds(2)).Join(Flow.Create<int>());

                var t = Source.From(Enumerable.Range(1, 100))
                    .Via(timeoutIdentity).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact(Skip = "Racy")]
        public void IdleTimeoutBidi_must_not_signal_error_if_traffic_is_one_way()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamWriter = this.CreatePublisherProbe<int>();
                var downstreamWriter = this.CreatePublisherProbe<string>();

                var upstream = Flow.FromSinkAndSource(Sink.Ignore<string>(),
                    Source.FromPublisher(upstreamWriter), Keep.Left);
                var downstream = Flow.FromSinkAndSource(Sink.Ignore<int>(),
                    Source.FromPublisher(downstreamWriter), Keep.Left);

                var assembly = upstream.JoinMaterialized(
                    BidiFlow.BidirectionalIdleTimeout<int, string>(TimeSpan.FromSeconds(2)),
                    Keep.Left).JoinMaterialized(downstream, Keep.Both);
                var r = assembly.Run(Materializer);
                var upFinished = r.Item1;
                var downFinished = r.Item2;

                upstreamWriter.SendNext(1);
                Thread.Sleep(1000);
                upstreamWriter.SendNext(1);
                Thread.Sleep(1000);
                upstreamWriter.SendNext(1);
                Thread.Sleep(1000);

                upstreamWriter.SendComplete();
                downstreamWriter.SendComplete();

                upFinished.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                downFinished.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            }, Materializer);
        }

        [Fact(Skip = "Racy")]
        public void IdleTimeoutBidi_must_be_able_to_signal_timeout_once_no_traffic_on_either_sides()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upWrite = this.CreatePublisherProbe<string>();
                var upRead = this.CreateSubscriberProbe<int>();

                var downWrite = this.CreatePublisherProbe<int>();
                var downRead = this.CreateSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var timeoutStage = b.Add(BidiFlow.BidirectionalIdleTimeout<string, int>(TimeSpan.FromSeconds(2)));

                    b.From(Source.FromPublisher(upWrite)).To(timeoutStage.Inlet1);
                    b.From(timeoutStage.Outlet1).To(Sink.FromSubscriber(downRead));
                    b.From(timeoutStage.Outlet2).To(Sink.FromSubscriber(upRead));
                    b.From(Source.FromPublisher(downWrite)).To(timeoutStage.Inlet2);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                // Request enough for the whole test
                upRead.Request(100);
                downRead.Request(100);

                upWrite.SendNext("DATA1");
                downRead.ExpectNext("DATA1");
                Thread.Sleep(1500);

                downWrite.SendNext(1);
                upRead.ExpectNext(1);
                Thread.Sleep(1500);

                upWrite.SendNext("DATA2");
                downRead.ExpectNext("DATA2");
                Thread.Sleep(1000);

                downWrite.SendNext(2);
                upRead.ExpectNext(2);

                upRead.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                var error1 = upRead.ExpectError();
                var error2 = downRead.ExpectError();

                error1.Should().BeOfType<TimeoutException>();
                error1.Message.Should().Be($"No elements passed in the last {TimeSpan.FromSeconds(2)}.");
                error2.ShouldBeEquivalentTo(error1);

                upWrite.ExpectCancellation();
                downWrite.ExpectCancellation();
            }, Materializer);
        }

        [Fact]
        public void IdleTimeoutBidi_must_signal_error_to_all_outputs()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upWrite = this.CreatePublisherProbe<string>();
                var upRead = this.CreateSubscriberProbe<int>();

                var downWrite = this.CreatePublisherProbe<int>();
                var downRead = this.CreateSubscriberProbe<string>();

                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var timeoutStage = b.Add(BidiFlow.BidirectionalIdleTimeout<string, int>(TimeSpan.FromSeconds(2)));

                    b.From(Source.FromPublisher(upWrite)).To(timeoutStage.Inlet1);
                    b.From(timeoutStage.Outlet1).To(Sink.FromSubscriber(downRead));
                    b.From(timeoutStage.Outlet2).To(Sink.FromSubscriber(upRead));
                    b.From(Source.FromPublisher(downWrite)).To(timeoutStage.Inlet2);

                    return ClosedShape.Instance;
                })).Run(Materializer);

                var te = new TestException("test");

                upWrite.SendError(te);

                upRead.ExpectSubscriptionAndError().ShouldBeEquivalentTo(te);
                downRead.ExpectSubscriptionAndError().ShouldBeEquivalentTo(te);
                downWrite.ExpectCancellation();
            }, Materializer);
        }
    }
}
