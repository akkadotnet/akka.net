using System;
using System.Linq;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Dsl
{
    public class SubscriberSourceSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public SubscriberSourceSpec()
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_SubscriberSource_must_be_able_to_use_Subscribe_in_materialized_value_transformation()
        {
            var f = Source.AsSubscriber<int>()
                .MapMaterializedValue(
                    s => Source.From(Enumerable.Range(1, 3)).RunWith(Sink.FromSubscriber<int, Unit>(s), Materializer))
                .RunWith(Sink.Fold<int, int>(0, (sum, i) => sum + i), Materializer);

            f.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            f.Result.Should().Be(6);
        }
    }
}
