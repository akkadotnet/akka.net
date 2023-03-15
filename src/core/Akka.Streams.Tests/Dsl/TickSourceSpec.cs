//-----------------------------------------------------------------------
// <copyright file="TickSourceSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class TickSourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public TickSourceSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_produce_ticks()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                
                var sub = await c.ExpectSubscriptionAsync();
                
                sub.Request(2);
                await c.ExpectNextAsync("tick");
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await c.ExpectNextAsync("tick");
                
                sub.Cancel();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_drop_ticks_when_not_requested()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                
                var sub = await c.ExpectSubscriptionAsync();
                
                sub.Request(2);
                await c.ExpectNextAsync("tick");
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await c.ExpectNextAsync("tick");
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(1400));
                
                sub.Request(2);
                await c.ExpectNextAsync("tick");
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await c.ExpectNextAsync("tick");
                
                sub.Cancel();
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_reject_multiple_subscribers_but_keep_the_first()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var p = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                    .RunWith(Sink.AsPublisher<string>(false), Materializer);
                var c1 = this.CreateManualSubscriberProbe<string>();
                var c2 = this.CreateManualSubscriberProbe<string>();
                p.Subscribe(c1);
                p.Subscribe(c2);
                
                var sub1 = await c1.ExpectSubscriptionAsync();
                await c2.ExpectSubscriptionAndErrorAsync();
                
                sub1.Request(1);
                await c1.ExpectNextAsync("tick");
                await c1.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                
                sub1.Request(2);
                await c1.ExpectNextAsync("tick");
                sub1.Cancel();
            }, Materializer);
        }

        [LocalFact(SkipLocal = "Racy. See https://github.com/akkadotnet/akka.net/pull/4424#issuecomment-632284459")]
        public async Task A_Flow_based_on_a_tick_publisher_must_be_usable_with_zip_for_a_simple_form_of_rate_limiting()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                RunnableGraph.FromGraph(GraphDsl.Create(b =>
                {
                    var zip = b.Add(new Zip<int, string>());
                    b.From(Source.From(Enumerable.Range(1, 100))).To(zip.In0);
                    b.From(Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick")
                        .MapMaterializedValue(_ => NotUsed.Instance)).To(zip.In1);
                    b.From(zip.Out)
                        .Via(Flow.Create<(int, string)>().Select(t => t.Item1))
                        .To(Sink.FromSubscriber(c));
                    return ClosedShape.Instance;
                })).Run(Materializer);
                
                var sub = await c.ExpectSubscriptionAsync();
                
                sub.Request(1000);
                await c.ExpectNextAsync(1);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await c.ExpectNextAsync(2);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                sub.Cancel();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_be_possible_to_cancel()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                
                sub.Request(2);
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(600));
                await c.ExpectNextAsync("tick");
                await c.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(200));
                await c.ExpectNextAsync("tick");
                
                cancelable.Cancel();
                await AwaitConditionAsync(() => Task.FromResult(cancelable.IsCancellationRequested));
                
                sub.Request(3);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }
        
        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_have_IsCancelled_mirror_the_cancellation_state()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                var sub = await c.ExpectSubscriptionAsync();
                
                sub.Request(2);
                await c.ExpectNextAsync("tick");
                cancelable.IsCancellationRequested.Should().BeFalse();
                cancelable.Cancel();
                cancelable.IsCancellationRequested.Should().BeTrue();
                await c.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task A_Flow_based_on_a_tick_publisher_must_support_being_cancelled_immediately_after_its_materialization()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var c = this.CreateManualSubscriberProbe<string>();
                var tickSource = Source.Tick(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(500), "tick");
                var cancelable = tickSource.To(Sink.FromSubscriber(c)).Run(Materializer);
                cancelable.Cancel();
                
                var sub = await c.ExpectSubscriptionAsync();
                sub.Request(2);
                await c.ExpectCompleteAsync();
            }, Materializer);
        }
    }
}
