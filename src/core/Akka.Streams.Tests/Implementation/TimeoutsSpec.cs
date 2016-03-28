using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Threading;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using TestPublisher = Akka.Streams.TestKit.TestPublisher;

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
        public void InitialTimeout_must_pass_through_elemnts_unmodified()
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
                var t = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .InitialTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Exception.Flatten().InnerExceptions.Any(e => e is TestException && e.Message.Equals("test"));

            }, Materializer);
        }

        [Fact]
        public void InitialTimeout_must_fail_if_no_inital_element_passes_until_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstreamProbe = this.CreateProbe<int>();
                Source.Maybe<int>()
                .InitialTimeout(TimeSpan.FromSeconds(1))
                .RunWith(Sink.FromSubscriber<int, Unit>(downstreamProbe), Materializer);
               
                downstreamProbe.ExpectSubscription();
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500));

                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be("The first element has not yet passed through in 1 second.");
            }, Materializer);
        }


        [Fact]
        public void CompletionTimeout_must_pass_through_elemnts_unmodified()
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
                var t = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .CompletionTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Exception.Flatten().InnerExceptions.Any(e => e is TestException && e.Message.Equals("test"));

            }, Materializer);
        }
        
        [Fact]
        public void CompletionTimeout_must_fail_if_not_completed_until_timeout()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamProbe = TestPublisher.CreateProbe<int>(this);
                var downstreamProbe = this.CreateProbe<int>();

                Source.FromPublisher<int, Unit>(upstreamProbe)
                    .CompletionTimeout(TimeSpan.FromSeconds(2))
                    .RunWith(Sink.FromSubscriber<int, Unit>(downstreamProbe), Materializer);


                upstreamProbe.SendNext(1);
                downstreamProbe.RequestNext(1);
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet

                upstreamProbe.SendNext(2);
                downstreamProbe.RequestNext(2);
                downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet

                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be("The stream has not been completed in 2 seconds.");
            }, Materializer);
        }


        [Fact]
        public void IdleTimeout_must_pass_through_elemnts_unmodified()
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
                var t = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .IdleTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Exception.Flatten().InnerExceptions.Any(e => e is TestException && e.Message.Equals("test"));

            }, Materializer);
        }

        [Fact]
        public void IdleTimeout_must_fail_if_time_between_elements_is_too_large()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamProbe = TestPublisher.CreateProbe<int>(this);
                var downstreamProbe = this.CreateProbe<int>();

                Source.FromPublisher<int, Unit>(upstreamProbe)
                    .IdleTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber<int, Unit>(downstreamProbe), Materializer);

                // Two seconds in overall, but won't timeout until time between elements is large enough
                // (i.e. this works differently from completionTimeout)
                for (var i = 1; i <= 4; i++)
                {
                    upstreamProbe.SendNext(1);
                    downstreamProbe.RequestNext(1);
                    downstreamProbe.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); // No timeout yet   
                }
                
                var ex = downstreamProbe.ExpectError();
                ex.Message.Should().Be("No elements passed in the last 1 second.");
            }, Materializer);
        }


        [Fact]
        public void IdleTimeoutBidi_must_not_signal_error_in_simple_loopback_case_and_pass_through_elements_unmodified()
        {
            this.AssertAllStagesStopped(() =>
            {
                var timeoutIdentity = BidiFlow.BidirectionalIdleTimeout<int, int>(TimeSpan.FromSeconds(2)).Join(Flow.Create<int>());

                var t = Source.From(Enumerable.Range(1, 100))
                    .Via(timeoutIdentity.Grouped(200))
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                t.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
                t.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public void IdleTimeoutBidi_must_not_signal_error_if_traffic_is_one_way()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstreamWriter = TestPublisher.CreateProbe<int>(this);
                var downstreamWriter = TestPublisher.CreateProbe<string>(this);

                var upstream = Flow.FromSinkAndSource(Sink.Ignore<string>(),
                    Source.FromPublisher<int, Unit>(upstreamWriter), Keep.Left);
                var downstream = Flow.FromSinkAndSource(Sink.Ignore<int>(),
                    Source.FromPublisher<string, Unit>(downstreamWriter), Keep.Left);

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

        [Fact]
        public void IdleTimeoutBidi_must_be_able_to_signal_timeout_once_no_traffic_on_either_sides()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upWrite = TestPublisher.CreateProbe<string>(this);
                var upRead = TestSubscriber.CreateProbe<int>(this);

                var downWrite = TestPublisher.CreateProbe<int>(this);
                var downRead = TestSubscriber.CreateProbe<string>(this);

                RunnableGraph<Unit>.FromGraph(GraphDsl.Create<ClosedShape, Unit>(b =>
                {
                    var timeoutStage = b.Add(BidiFlow.BidirectionalIdleTimeout<string, int>(TimeSpan.FromSeconds(2)));

                    b.From(Source.FromPublisher<string, Unit>(upWrite)).To(timeoutStage.Inlet1);
                    b.From(timeoutStage.Outlet1).To(Sink.FromSubscriber<string, Unit>(downRead));
                    b.From(timeoutStage.Outlet2).To(Sink.FromSubscriber<int, Unit>(upRead));
                    b.From(Source.FromPublisher<int, Unit>(downWrite)).To(timeoutStage.Inlet2);

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
                error1.Message.Should().Be("No elements passed in the last 2 seconds.");
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
                var upWrite = TestPublisher.CreateProbe<string>(this);
                var upRead = TestSubscriber.CreateProbe<int>(this);

                var downWrite = TestPublisher.CreateProbe<int>(this);
                var downRead = TestSubscriber.CreateProbe<string>(this);

                RunnableGraph<Unit>.FromGraph(GraphDsl.Create<ClosedShape, Unit>(b =>
                {
                    var timeoutStage = b.Add(BidiFlow.BidirectionalIdleTimeout<string, int>(TimeSpan.FromSeconds(2)));

                    b.From(Source.FromPublisher<string, Unit>(upWrite)).To(timeoutStage.Inlet1);
                    b.From(timeoutStage.Outlet1).To(Sink.FromSubscriber<string, Unit>(downRead));
                    b.From(timeoutStage.Outlet2).To(Sink.FromSubscriber<int, Unit>(upRead));
                    b.From(Source.FromPublisher<int, Unit>(downWrite)).To(timeoutStage.Inlet2);

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
