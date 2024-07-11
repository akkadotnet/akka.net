//-----------------------------------------------------------------------
// <copyright file="TimeoutsSpec.cs" company="Akka.NET Project">
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
using Akka.TestKit.Extensions;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using Xunit.Abstractions;
using static FluentAssertions.FluentActions;

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
        public async Task InitialTimeout_must_pass_through_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .InitialTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await t.ShouldCompleteWithin(3.Seconds());
                t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }
        
        [Fact]
        public async Task InitialTimeout_must_pass_through_error_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .InitialTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await Awaiting(() => task.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<TestException>().WithMessage("test");
            }, Materializer);
        }

        [Fact]
        public async Task InitialTimeout_must_fail_if_no_initial_element_passes_until_timeout()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var downstreamProbe = this.CreateSubscriberProbe<int>();
                Source.Maybe<int>()
                    .InitialTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(downstreamProbe), Materializer);

                await downstreamProbe.ExpectSubscriptionAsync();
                await downstreamProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

                var ex = await downstreamProbe.ExpectErrorAsync();
                ex.Message.Should().Be($"The first element has not yet passed through in {TimeSpan.FromSeconds(1)}.");
            }, Materializer);
        }


        [Fact]
        public async Task CompletionTimeout_must_pass_through_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .CompletionTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await t.ShouldCompleteWithin(3.Seconds());
                t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public async Task CompletionTimeout_must_pass_through_error_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .CompletionTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await Awaiting(() => task.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<TestException>().WithMessage("test");
            }, Materializer);
        }
        
        [Fact]
        public async Task CompletionTimeout_must_fail_if_not_completed_until_timeout()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstreamProbe = this.CreatePublisherProbe<int>();
                var downstreamProbe = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstreamProbe)
                    .CompletionTimeout(TimeSpan.FromSeconds(2))
                    .RunWith(Sink.FromSubscriber(downstreamProbe), Materializer);


                upstreamProbe.SendNext(1);
                await downstreamProbe.AsyncBuilder()
                    .RequestNext(1)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(500)) // No timeout yet
                    .ExecuteAsync();

                upstreamProbe.SendNext(2);
                await downstreamProbe.AsyncBuilder()
                    .RequestNext(2)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(500)) // No timeout yet
                    .ExecuteAsync();

                var ex = await downstreamProbe.ExpectErrorAsync();
                ex.Message.Should().Be($"The stream has not been completed in {TimeSpan.FromSeconds(2)}.");
            }, Materializer);
        }


        [Fact]
        public async Task IdleTimeout_must_pass_through_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var t = Source.From(Enumerable.Range(1, 100))
                    .IdleTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await t.ShouldCompleteWithin(3.Seconds());
                t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public async Task IdleTimeout_must_pass_through_error_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .Concat(Source.Failed<int>(new TestException("test")))
                    .IdleTimeout(TimeSpan.FromSeconds(2)).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await Awaiting(() => task.ShouldCompleteWithin(3.Seconds()))
                    .Should().ThrowAsync<TestException>().WithMessage("test");
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task IdleTimeout_must_fail_if_time_between_elements_is_too_large()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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
                    await downstreamProbe.AsyncBuilder()
                        .RequestNext(1)
                        .ExpectNoMsg(TimeSpan.FromMilliseconds(500)) // No timeout yet
                        .ExecuteAsync();
                }
                
                var ex = await downstreamProbe.ExpectErrorAsync();
                ex.Message.Should().Be($"No elements passed in the last {TimeSpan.FromSeconds(1)}.");
            }, Materializer);
        }
        

        [Fact]
        public async Task BackpressureTimeout_must_pass_through_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var task = Source.From(Enumerable.Range(1, 100))
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await task.ShouldCompleteWithin(3.Seconds());
                task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [Fact]
        public async Task BackpressureTimeout_must_succeed_if_subscriber_demand_arrives()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.From(new[] { 1, 2, 3, 4 })
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                for (var i = 1; i < 4; i++)
                {
                    await subscriber.AsyncBuilder()
                        .RequestNext(i)
                        .ExpectNoMsg(TimeSpan.FromMilliseconds(250))
                        .ExecuteAsync();
                }

                await subscriber.AsyncBuilder()
                    .RequestNext(4)
                    .ExpectComplete()
                    .ExecuteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task BackpressureTimeout_must_not_throw_if_publisher_is_less_frequent_than_timeout()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = this.CreatePublisherProbe<string>();
                var subscriber = this.CreateSubscriberProbe<string>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                await subscriber.AsyncBuilder()
                    .Request(2)
                    .ExpectNoMsg(TimeSpan.FromSeconds(1))
                    .ExecuteAsync();

                publisher.SendNext("Quick Msg");
                await subscriber.AsyncBuilder()
                    .ExpectNext("Quick Msg")
                    .ExpectNoMsg(TimeSpan.FromSeconds(3))
                    .ExecuteAsync();
                
                publisher.SendNext("Slow Msg");
                await subscriber.ExpectNextAsync("Slow Msg");

                publisher.SendComplete();
                await subscriber.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task BackpressureTimeout_must_not_throw_if_publisher_wont_perform_emission_ever()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = this.CreatePublisherProbe<string>();
                var subscriber = this.CreateSubscriberProbe<string>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                await subscriber.AsyncBuilder()
                    .Request(16)
                    .ExpectNoMsg(TimeSpan.FromSeconds(2))
                    .ExecuteAsync();

                publisher.SendComplete();
                await subscriber.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task BackpressureTimeout_must_throw_if_subscriber_wont_generate_demand_on_time()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                await subscriber.RequestAsync(1);
                publisher.SendNext(1);
                await subscriber.ExpectNextAsync(1);

                await Task.Delay(3.Seconds());

                (await subscriber.ExpectErrorAsync()).Message.Should().Be("No demand signalled in the last 00:00:01.");
            }, Materializer);
        }
        
        [Fact]
        public async Task BackpressureTimeout_must_throw_if_subscriber_never_generate_demand()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                await subscriber.ExpectSubscriptionAsync();

                await Task.Delay(3.Seconds());

                (await subscriber.ExpectErrorAsync()).Message.Should().Be("No demand signalled in the last 00:00:01.");
            }, Materializer);
        }
        
        [Fact]
        public async Task BackpressureTimeout_must_not_throw_if_publisher_completes_without_fulfilling_subscribers_demand()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var publisher = this.CreatePublisherProbe<int>();
                var subscriber = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(publisher)
                    .BackpressureTimeout(TimeSpan.FromSeconds(1))
                    .RunWith(Sink.FromSubscriber(subscriber), Materializer);

                await subscriber.RequestAsync(2);
                publisher.SendNext(1);
                await subscriber.ExpectNextAsync(1);

                await subscriber.ExpectNoMsgAsync(TimeSpan.FromSeconds(2));

                publisher.SendComplete();
                await subscriber.ExpectCompleteAsync();
            }, Materializer);
        }


        [Fact()]
        public async Task IdleTimeoutBidi_must_not_signal_error_in_simple_loopback_case_and_pass_through_elements_unmodified()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var timeoutIdentity = BidiFlow.BidirectionalIdleTimeout<int, int>(TimeSpan.FromSeconds(2)).Join(Flow.Create<int>());

                var t = Source.From(Enumerable.Range(1, 100))
                    .Via(timeoutIdentity).Grouped(200)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                await t.ShouldCompleteWithin(3.Seconds());
                t.Result.Should().BeEquivalentTo(Enumerable.Range(1, 100));
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy in AzDo CI/CD")]
        public async Task IdleTimeoutBidi_must_not_signal_error_if_traffic_is_one_way()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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
                var (upFinished, downFinished) = assembly.Run(Materializer);

                upstreamWriter.SendNext(1);
                await Task.Delay(1.Seconds());
                upstreamWriter.SendNext(1);
                await Task.Delay(1.Seconds());
                upstreamWriter.SendNext(1);
                await Task.Delay(1.Seconds());

                upstreamWriter.SendComplete();
                downstreamWriter.SendComplete();

                await upFinished.ShouldCompleteWithin(3.Seconds());
                await downFinished.ShouldCompleteWithin(3.Seconds());
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task IdleTimeoutBidi_must_be_able_to_signal_timeout_once_no_traffic_on_either_sides()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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
                await upRead.RequestAsync(100);
                await downRead.RequestAsync(100);

                upWrite.SendNext("DATA1");
                await downRead.ExpectNextAsync("DATA1");
                await Task.Delay(1.5.Seconds());

                downWrite.SendNext(1);
                await upRead.ExpectNextAsync(1);
                await Task.Delay(1.5.Seconds());

                upWrite.SendNext("DATA2");
                await downRead.ExpectNextAsync("DATA2");
                await Task.Delay(1.Seconds());

                downWrite.SendNext(2);
                await upRead.ExpectNextAsync(2);

                await upRead.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));
                var error1 = await upRead.ExpectErrorAsync();
                var error2 = await downRead.ExpectErrorAsync();

                error1.Should().BeOfType<TimeoutException>();
                error1.Message.Should().Be($"No elements passed in the last {TimeSpan.FromSeconds(2)}.");
                error2.Should().BeEquivalentTo(error1);

                await upWrite.ExpectCancellationAsync();
                await downWrite.ExpectCancellationAsync();
            }, Materializer);
        }

        [Fact]
        public async Task IdleTimeoutBidi_must_signal_error_to_all_outputs()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
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

                await upWrite.SendErrorAsync(te);

                (await upRead.ExpectSubscriptionAndErrorAsync()).Should().BeEquivalentTo(te);
                (await downRead.ExpectSubscriptionAndErrorAsync()).Should().BeEquivalentTo(te);
                await downWrite.ExpectCancellationAsync();
            }, Materializer);
        }
    }
}
