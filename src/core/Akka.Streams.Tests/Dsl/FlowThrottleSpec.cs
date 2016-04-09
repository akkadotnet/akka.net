using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using Akka.IO;
using Akka.Streams.Actors;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class FlowThrottleSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowThrottleSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static ByteString GenerateByteString(int length)
        {
            var random = new Random();
            var bytes =
                Enumerable.Range(0, 255)
                    .Select(_ => random.Next(0, 255))
                    .Take(length)
                    .Select(Convert.ToByte)
                    .ToArray();
            return ByteString.Create(bytes);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_work_for_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 0, ThrottleMode.Shaping)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNext(1, 2, 3, 4, 5)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_emit_single_element_per_tick()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(1, TimeSpan.FromMilliseconds(300), 0, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(2);
                upstream.SendNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(150));
                downstream.ExpectNext(1);

                upstream.SendNext(2);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(150));
                downstream.ExpectNext(2);

                upstream.SendComplete();
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_not_send_downstream_if_upstream_does_not_emit_element()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(1, TimeSpan.FromMilliseconds(300), 0, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(2);
                upstream.SendNext(1);
                downstream.ExpectNext(1);

                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                upstream.SendNext(2);
                downstream.ExpectNext(2);

                upstream.SendComplete();
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_cancel_when_downstream_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstream = TestSubscriber.CreateProbe<int>(this);
                Source.From(Enumerable.Range(1, 10))
                    .Throttle(1, TimeSpan.FromMilliseconds(300), 0, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);
                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_send_elements_downstream_as_soon_as_time_comes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe =
                    Source.From(Enumerable.Range(1, 10))
                        .Throttle(2, TimeSpan.FromMilliseconds(500), 0, ThrottleMode.Shaping)
                        .RunWith(this.SinkProbe<int>(), Materializer);
                probe.Request(5);
                var result = probe.ReceiveWhile(TimeSpan.FromMilliseconds(600), filter: x => x);
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExpectNext(3)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExpectNext(4);
                probe.Cancel();
                // assertion may take longer then the throttle and therefore the next assertion fails
                result.ShouldAllBeEquivalentTo(new[] { new OnNext(1), new OnNext(2) });
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_burst_according_to_its_maximum_if_enough_time_passed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(1, TimeSpan.FromMilliseconds(200), 5, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                upstream.SendNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream.ExpectNext(1);
                downstream.Request(5);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(1200));
                var expected = new List<OnNext>();
                for (var i = 2; i < 7; i++)
                {
                    upstream.SendNext(i);
                    expected.Add(new OnNext(i));
                }
                downstream.ReceiveWhile(TimeSpan.FromMilliseconds(300), filter: x => x, msgs: 5)
                    .ShouldAllBeEquivalentTo(expected);
                
                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_burst_some_elements_if_have_enough_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(1, TimeSpan.FromMilliseconds(200), 5, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                upstream.SendNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream.ExpectNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                downstream.Request(5);
                var expected = new List<OnNext>();
                for (var i = 2; i < 5; i++)
                {
                    upstream.SendNext(i);
                    if (i < 4)
                        expected.Add(new OnNext(i));
                }
                downstream.ReceiveWhile(TimeSpan.FromMilliseconds(100), filter: x => x, msgs: 2)
                    .ShouldAllBeEquivalentTo(expected);

                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_throw_exception_when_exceeding_throughtput_in_enforced_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    Source.From(Enumerable.Range(1, 5))
                        .Throttle(1, TimeSpan.FromMilliseconds(200), 5, ThrottleMode.Enforcing)
                        .RunWith(Sink.Ignore<int>(), Materializer);
                t.Invoking(task => task.Wait(TimeSpan.FromSeconds(2))).ShouldThrow<OverflowException>();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_single_cost_elements_must_properly_combine_shape_and_throttle_modes()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 5, ThrottleMode.Shaping)
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 5, ThrottleMode.Enforcing)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNext(1, 2, 3, 4, 5)
                    .ExpectComplete();
            }, Materializer);
        }



        [Fact]
        public void Throttle_for_various_cost_elements_must_work_for_the_happy_case()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 0, _ => 1, ThrottleMode.Shaping)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNext(1, 2, 3, 4, 5)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_emit_elements_according_to_cost()
        {
            this.AssertAllStagesStopped(() =>
            {
                var list = Enumerable.Range(1, 4).Select(x => x*2).Select(GenerateByteString).ToList();

                Source.From(list)
                    .Throttle(2, TimeSpan.FromMilliseconds(200), 0, x => x.Count, ThrottleMode.Shaping)
                    .RunWith(this.SinkProbe<ByteString>(), Materializer)
                    .Request(4)
                    .ExpectNext(list[0])
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(300))
                    .ExpectNext(list[1])
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(500))
                    .ExpectNext(list[2])
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(700))
                    .ExpectNext(list[3])
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_not_send_downstream_if_upstream_does_not_emit_element()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(1, TimeSpan.FromMilliseconds(300), 0, x => x, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(2);
                upstream.SendNext(1);
                downstream.ExpectNext(1);

                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                upstream.SendNext(2);
                downstream.ExpectNext(2);

                upstream.SendComplete();
                downstream.ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_cancel_when_downstream_cancels()
        {
            this.AssertAllStagesStopped(() =>
            {
                var downstream = TestSubscriber.CreateProbe<int>(this);
                Source.From(Enumerable.Range(1, 10))
                    .Throttle(2, TimeSpan.FromMilliseconds(200), 0, x => x, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);
                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_send_elements_downstream_as_soon_as_time_comes()
        {
            this.AssertAllStagesStopped(() =>
            {
                var probe =
                    Source.From(Enumerable.Range(1, 10))
                        .Throttle(4, TimeSpan.FromMilliseconds(500), 0, _ => 2, ThrottleMode.Shaping)
                        .RunWith(this.SinkProbe<int>(), Materializer);
                probe.Request(5);
                var result = probe.ReceiveWhile(TimeSpan.FromMilliseconds(600), filter: x => x);
                probe.ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExpectNext(3)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(100))
                    .ExpectNext(4);
                probe.Cancel();
                // assertion may take longer then the throttle and therefore the next assertion fails
                result.ShouldAllBeEquivalentTo(new[] { new OnNext(1), new OnNext(2) });
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_burst_according_to_its_maximum_if_enough_time_passed()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(2, TimeSpan.FromMilliseconds(400), 5, x => 1, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                upstream.SendNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream.ExpectNext(1);
                downstream.Request(5);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(1200));
                var expected = new List<OnNext>();
                for (var i = 2; i < 7; i++)
                {
                    upstream.SendNext(i);
                    expected.Add(new OnNext(i));
                }
                downstream.ReceiveWhile(TimeSpan.FromMilliseconds(300), filter: x => x, msgs: 5)
                    .ShouldAllBeEquivalentTo(expected);

                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_burst_some_elements_if_have_enough_time()
        {
            this.AssertAllStagesStopped(() =>
            {
                var upstream = TestPublisher.CreateProbe<int>(this);
                var downstream = TestSubscriber.CreateProbe<int>(this);

                Source.FromPublisher(upstream)
                    .Throttle(2, TimeSpan.FromMilliseconds(400), 5, e => e < 4 ? 1 : 20, ThrottleMode.Shaping)
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                upstream.SendNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(100));
                downstream.ExpectNext(1);
                downstream.ExpectNoMsg(TimeSpan.FromMilliseconds(500)); //wait to receive 2 in burst afterwards
                downstream.Request(5);
                var expected = new List<OnNext>();
                for (var i = 2; i < 5; i++)
                {
                    upstream.SendNext(i);
                    if (i < 4)
                        expected.Add(new OnNext(i));
                }
                downstream.ReceiveWhile(TimeSpan.FromMilliseconds(200), filter: x => x, msgs: 2)
                    .ShouldAllBeEquivalentTo(expected);

                downstream.Cancel();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_throw_exception_when_exceeding_throughtput_in_enforced_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var t =
                    Source.From(Enumerable.Range(1, 5))
                        .Throttle(2, TimeSpan.FromMilliseconds(200), 5, x => x, ThrottleMode.Enforcing)
                        .RunWith(Sink.Ignore<int>(), Materializer);
                t.Invoking(task => task.Wait(TimeSpan.FromSeconds(2))).ShouldThrow<OverflowException>();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_properly_combine_shape_and_throttle_modes()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 5))
                    .Throttle(2, TimeSpan.FromMilliseconds(200), 0, x => x, ThrottleMode.Shaping)
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 5, ThrottleMode.Enforcing)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNext(1, 2, 3, 4, 5)
                    .ExpectComplete();
            }, Materializer);
        }

        [Fact]
        public void Throttle_for_various_cost_elements_must_handle_rate_calculation_function_exception()
        {
            this.AssertAllStagesStopped(() =>
            {
                var ex = new SystemException();
                Source.From(Enumerable.Range(1, 5))
                    .Throttle(2, TimeSpan.FromMilliseconds(200), 0, _ => { throw ex; }, ThrottleMode.Shaping)
                    .Throttle(1, TimeSpan.FromMilliseconds(100), 5, ThrottleMode.Enforcing)
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectError().Should().Be(ex);
            }, Materializer);
        }
    }
}
