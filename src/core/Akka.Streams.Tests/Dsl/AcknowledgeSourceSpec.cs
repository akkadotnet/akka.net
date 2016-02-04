using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class AcknowledgeSourceSpec : AkkaSpec
    {
        private readonly ActorMaterializer materializer;

        public AcknowledgeSourceSpec(ITestOutputHelper output) : base(output)
        {
            materializer = Sys.Materializer();
        }

        private void AssertSuccess(Task<bool> future, bool value)
        {
            future.Wait(TimeSpan.FromSeconds(1)).Should().Be(value);
        }

        [Fact]
        public void AcknowledgeSource_should_emit_received_message_to_the_stream()
        {
            var s = this.CreateManualProbe<int>();
            var queue = Source.Queue<int>(10, OverflowStrategy.Fail).To(Sink.FromSubscriber(s)).Run(materializer);
            var sub = s.ExpectSubscription();

            sub.Request(2);
            AssertSuccess(queue.OfferAsync(1), true);
            s.ExpectNext(1);
            AssertSuccess(queue.OfferAsync(2), true);
            s.ExpectNext(2);
            AssertSuccess(queue.OfferAsync(3), true);
            sub.Cancel();
        }

        [Fact]
        public void AcknowledgeSource_should_buffer_when_needed()
        {
            var s = this.CreateManualProbe<int>();
            var queue = Source.Queue<int>(100, OverflowStrategy.DropHead).To(Sink.FromSubscriber(s)).Run(materializer);
            var sub = s.ExpectSubscription();

            for (int i = 1; i <= 20; i++) AssertSuccess(queue.OfferAsync(i), true);
            sub.Request(10);
            for (int i = 1; i <= 10; i++) AssertSuccess(queue.OfferAsync(i), true);
            sub.Request(10);
            for (int i = 11; i <= 20; i++) AssertSuccess(queue.OfferAsync(i), true);

            for (int i = 200; i <= 399; i++) AssertSuccess(queue.OfferAsync(i), true);
            sub.Request(100);
            for (int i = 300; i <= 399; i++) AssertSuccess(queue.OfferAsync(i), true);
            sub.Cancel();
        }

        [Fact]
        public void AcknowledgeSource_should_not_fail_when_0_buffer_space_and_demand_is_signalled()
        {
            var s = this.CreateManualProbe<int>();
            var queue = Source.Queue<int>(0, OverflowStrategy.DropHead).To(Sink.FromSubscriber(s)).Run(materializer);
            var sub = s.ExpectSubscription();

            sub.Request(1);
            AssertSuccess(queue.OfferAsync(1), true);
            s.ExpectNext(1);
            sub.Cancel();
        }

        [Fact]
        public void AcknowledgeSource_should_return_false_when_can_reject_element_to_buffer()
        {
            var s = this.CreateManualProbe<int>();
            var queue = Source.Queue<int>(1, OverflowStrategy.DropNew).To(Sink.FromSubscriber(s)).Run(materializer);
            var sub = s.ExpectSubscription();

            AssertSuccess(queue.OfferAsync(1), true);
            AssertSuccess(queue.OfferAsync(2), false);
            sub.Request(1);
            s.ExpectNext(1);
            sub.Cancel();
        }

        [Fact]
        public void AcknowledgeSource_should_wait_when_buffer_is_full_and_backpressure_is_on()
        {
            var s = this.CreateManualProbe<int>();
            var queue = Source.Queue<int>(2, OverflowStrategy.Backpressure).To(Sink.FromSubscriber(s)).Run(materializer);
            var sub = s.ExpectSubscription();

            AssertSuccess(queue.OfferAsync(1), true);
            var addedSecond = queue.OfferAsync(2);
            addedSecond.PipeTo(TestActor);
            ExpectNoMsg(TimeSpan.FromMilliseconds(300));

            sub.Request(1);
            s.ExpectNext(1);
            AssertSuccess(addedSecond, true);

            sub.Request(1);
            s.ExpectNext(2);
                
            sub.Cancel();
        }
    }
}