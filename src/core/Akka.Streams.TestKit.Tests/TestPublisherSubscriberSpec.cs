//-----------------------------------------------------------------------
// <copyright file="TestPublisherSubscriberSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    public class TestPublisherSubscriberSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public TestPublisherSubscriberSpec(ITestOutputHelper output = null) : base(output)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(initialSize: 2, maxSize: 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public async Task TestPublisher_and_TestSubscriber_should_have_all_events_accessible_from_manual_probes()
        {
            await this.AssertAllStagesStoppedAsync( async() =>
            {
                var upstream = this.CreateManualPublisherProbe<int>();
                var downstream = this.CreateManualSubscriberProbe<int>();
                Source.FromPublisher(upstream)
                    .RunWith(Sink.AsPublisher<int>(false), Materializer)
                    .Subscribe(downstream);

                var upstreamSubscription = await upstream.ExpectSubscriptionAsync();
                object evt = await downstream.ExpectEventAsync();
                evt.Should().BeOfType<TestSubscriber.OnSubscribe>();
                var downstreamSubscription = ((TestSubscriber.OnSubscribe) evt).Subscription;

                upstreamSubscription.SendNext(1);
                downstreamSubscription.Request(1);
                evt = await upstream.ExpectEventAsync();
                evt.Should().BeOfType<TestPublisher.RequestMore>();
                ((TestPublisher.RequestMore) evt).NrOfElements.Should().Be(1);
                evt = await downstream.ExpectEventAsync();
                evt.Should().BeOfType<TestSubscriber.OnNext<int>>();
                ((TestSubscriber.OnNext<int>) evt).Element.Should().Be(1);

                upstreamSubscription.SendNext(1);
                downstreamSubscription.Request(1);
                await downstream.AsyncBuilder().ExpectNext(1).ExecuteAsync();

                upstreamSubscription.SendComplete();
                evt = await downstream.ExpectEventAsync();
                evt.Should().BeOfType<TestSubscriber.OnComplete>();
            }, Materializer);
        }

        // "handle gracefully partial function that is not suitable" does not apply

        [Fact]
        public async Task TestPublisher_and_TestSubscriber_should_properly_update_PendingRequest_in_ExpectRequest()
        {
            await this.AssertAllStagesStoppedAsync(async () =>
            {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream).RunWith(Sink.FromSubscriber(downstream), Materializer);

                (await downstream.ExpectSubscriptionAsync()).Request(10);

                (await upstream.ExpectRequestAsync()).Should().Be(10);
                upstream.SendNext(1);
                await downstream.AsyncBuilder().ExpectNext(1).ExecuteAsync();
            }, Materializer);
        }
    }
}
