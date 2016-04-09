//-----------------------------------------------------------------------
// <copyright file="TestPublisherSubscriberSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Reactive.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.TestKit.Tests
{
    public class TestPublisherSubscriberSpec : AkkaSpec
    {
        protected readonly ActorMaterializer Materializer;

        public TestPublisherSubscriberSpec(ITestOutputHelper output = null) : base(output)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(initialSize: 2, maxSize: 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void TestPublisher_and_TestSubscriber_should_have_all_events_accessible_from_manual_probes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateManualProbe<int>(this);
                var downstream = TestSubscriber.CreateManualProbe<int>(this);
                Source.FromPublisher(upstream)
                    .RunWith(Sink.AsPublisher<int>(false), Materializer)
                    .Subscribe(downstream);

                var upstreamSubscription = upstream.ExpectSubscription();
                object evt = downstream.ExpectEvent();
                evt.Should().BeOfType<TestSubscriber.OnSubscribe>();
                var downstreamSubscription = ((TestSubscriber.OnSubscribe) evt).Subscription;

                upstreamSubscription.SendNext(1);
                downstreamSubscription.Request(1);
                evt = upstream.ExpectEvent();
                evt.Should().BeOfType<TestPublisher.RequestMore>();
                ((TestPublisher.RequestMore) evt).NrOfElements.Should().Be(1);
                evt = downstream.ExpectEvent();
                evt.Should().BeOfType<TestSubscriber.OnNext<int>>();
                ((TestSubscriber.OnNext<int>) evt).Element.Should().Be(1);

                upstreamSubscription.SendNext(1);
                downstreamSubscription.Request(1);
                downstream.ExpectNext(1);

                upstreamSubscription.SendComplete();
                evt = downstream.ExpectEvent();
                evt.Should().BeOfType<TestSubscriber.OnComplete>();
            }, Materializer);
        }

        // "handle gracefully partial function that is not suitable" does not apply
    }
}