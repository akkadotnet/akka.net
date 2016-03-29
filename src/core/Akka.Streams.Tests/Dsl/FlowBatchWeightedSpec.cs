using System;
using System.Reactive.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowBatchWeightedSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowBatchWeightedSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(2, 2);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void BatchWeighted_must_not_aggregate_heavy_elements()
        {
            var publisher = TestPublisher.CreateProbe<int>(this);
            var subscriber = TestSubscriber.CreateProbe<int>(this);

            Source.FromPublisher<int, Unit>(publisher)
                .BatchWeighted(3, _ => 4, i => i, (sum, i) => sum + i)
                .To(Sink.FromSubscriber<int, Unit>(subscriber))
                .Run(Materializer);
            var sub = subscriber.EnsureSubscription();

            publisher.SendNext(1);
            publisher.SendNext(2);

            sub.Request(1);
            subscriber.ExpectNext(1);

            publisher.SendNext(3);
            subscriber.ExpectNoMsg(TimeSpan.FromSeconds(1));

            sub.Request(2);
            subscriber.ExpectNext(2);
            subscriber.ExpectNext(3);

            sub.Cancel();
        }
    }
}
