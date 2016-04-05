using System;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowFromFutureSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowFromFutureSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_produce_one_element_from_already_successful_Future()
        {
            var c = TestSubscriber.CreateManualProbe<int>(this);
            var p = Source.FromTask(Task.FromResult(1)).RunWith(Sink.AsPublisher<int>(true), Materializer);
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            sub.Request(1);
            c.ExpectNext(1);
            c.ExpectComplete();
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_produce_error_from_already_failed_Future()
        {
            var ex = new TestException("test");
            var c = TestSubscriber.CreateManualProbe<int>(this);
            var p = Source.FromTask(Task.Run(new Func<int>(()=> { throw ex; }))).RunWith(Sink.AsPublisher<int>(false), Materializer);
            p.Subscribe(c);
            c.ExpectSubscriptionAndError().Should().Be(ex);
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_produce_one_element_when_Future_is_completed()
        {
            var promies = new TaskCompletionSource<int>();
            var c = TestSubscriber.CreateManualProbe<int>(this);
            var p =Source.FromTask(promies.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            sub.Request(1);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
            promies.SetResult(1);
            c.ExpectNext(1);
            c.ExpectComplete();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_produce_one_element_when_Future_is_completed_but_not_before_request()
        {
            var promies = new TaskCompletionSource<int>();
            var c = TestSubscriber.CreateManualProbe<int>(this);
            var p = Source.FromTask(promies.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            promies.SetResult(1);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            sub.Request(1);
            c.ExpectNext(1);
            c.ExpectComplete();
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_produce_elements_with_multiple_subscribers()
        {
            var promise = new TaskCompletionSource<int>();
            var p = Source.FromTask(promise.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            var c1 = TestSubscriber.CreateManualProbe<int>(this);
            var c2 = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(c1);
            p.Subscribe(c2);
            var sub1 = c1.ExpectSubscription();
            var sub2 = c2.ExpectSubscription();
            sub1.Request(1);
            promise.SetResult(1);
            sub2.Request(2);
            c1.ExpectNext(1);
            c2.ExpectNext(1);
            c1.ExpectComplete();
            c2.ExpectComplete();
        }

        [Fact]
        public void A_Flow_based_on_a_Future_must_allow_cancel_before_receiving_element()
        {
            var promies = new TaskCompletionSource<int>();
            var c = TestSubscriber.CreateManualProbe<int>(this);
            var p = Source.FromTask(promies.Task).RunWith(Sink.AsPublisher<int>(true), Materializer);
            var keepAlive = TestSubscriber.CreateManualProbe<int>(this);
            p.Subscribe(keepAlive);
            p.Subscribe(c);
            var sub = c.ExpectSubscription();
            sub.Request(1);
            sub.Cancel();
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
            promies.SetResult(1);
            c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
        }
    }
}
