//-----------------------------------------------------------------------
// <copyright file="FlowDelaySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit;
using Akka.Tests.Shared.Internals;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    [Collection(nameof(FlowDelaySpec))] // timing sensitive since it involves hard delays
    public class FlowDelaySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDelaySpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact(Skip ="Racy")]
        public void A_Delay_must_deliver_elements_with_some_time_shift()
        {
            var task =
                Source.From(Enumerable.Range(1, 10))
                    .Delay(TimeSpan.FromSeconds(1))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
            task.Wait(TimeSpan.FromMilliseconds(1200)).Should().BeTrue();
            task.Result.Should().BeEquivalentTo(Enumerable.Range(1, 10));
        }

        // Was marked as racy.
        // Raised probe.ExpectNext from 300 to 600. 300 is flaky when CPU resources are scarce.
        // Passed 500 consecutive local test runs with no fail with very heavy load after modification
        [Fact]
        public void A_Delay_must_add_delay_to_initialDelay_if_exists_upstream()
        {
            var probe = Source.From(Enumerable.Range(1, 10))
                .InitialDelay(TimeSpan.FromSeconds(1))
                .Delay(TimeSpan.FromSeconds(1))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(10);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1800));
            probe.ExpectNext(1, TimeSpan.FromMilliseconds(600));
            probe.ExpectNextN(Enumerable.Range(2, 9));
            probe.ExpectComplete();
        }

        [Fact(Skip ="Racy")]
        public void A_Delay_must_deliver_element_after_time_passed_from_actual_receiving_element()
        {
            var probe = Source.From(Enumerable.Range(1, 3))
                .Delay(TimeSpan.FromMilliseconds(300))
                .RunWith(this.SinkProbe<int>(), Materializer);
            probe.Request(2)
                .ExpectNoMsg(TimeSpan.FromMilliseconds(200)) //delay
                .ExpectNext(1, TimeSpan.FromMilliseconds(200)) //delayed element
                .ExpectNext(2, TimeSpan.FromMilliseconds(100)) //buffered element
                .ExpectNoMsg(TimeSpan.FromMilliseconds(200));
            probe.Request(1)
                .ExpectNext(3) //buffered element
                .ExpectComplete();
        }

        [Fact(Skip ="Racy")]
        public void A_Delay_must_deliver_elements_with_delay_for_slow_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .Delay(TimeSpan.FromMilliseconds(300))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = c.ExpectSubscription();
                var pSub = p.ExpectSubscription();
                cSub.Request(100);
                pSub.SendNext(1);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                c.ExpectNext(1);
                pSub.SendNext(2);
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(200));
                c.ExpectNext(2);
                pSub.SendComplete();
                c.ExpectComplete();
            }, Materializer);
        }

        // Was marked as racy.
        // Raised task.Wait() from 1200 to 1800. 1200 is flaky when CPU resources are scarce.
        // Passed 500 consecutive local test runs with no fail with very heavy load after modification
        [Fact]
        public void A_Delay_must_drop_tail_for_internal_buffer_if_it_is_full_in_DropTail_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropTail)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Wait(TimeSpan.FromMilliseconds(1800)).Should().BeTrue();
                var expected = Enumerable.Range(1, 15).ToList();
                expected.Add(20);
                task.Result.Should().BeEquivalentTo(expected);
            }, Materializer);
        }

        // Was marked as racy.
        // Raised task.Wait() from 1200 to 1800. 1200 is flaky when CPU resources are scarce.
        // Passed 500 consecutive local test runs with no fail with very heavy load after modification
        [Fact]
        public void A_Delay_must_drop_head_for_internal_buffer_if_it_is_full_in_DropHead_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropHead)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Wait(TimeSpan.FromMilliseconds(1800)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(5, 16));
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_Delay_must_clear_all_for_internal_buffer_if_it_is_full_in_DropBuffer_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var task = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.DropBuffer)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);

                task.Wait(TimeSpan.FromMilliseconds(1200)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(17, 4));
            }, Materializer);
        }

        [Fact(Skip = "Extremely flaky because of the interleaved ExpectNext and ExpectNoMsg with a very tight timing requirement. .Net timer implementation is not consistent enough to maintain accurate timing under heavy CPU load.")]
        public void A_Delay_must_pass_elements_with_delay_through_normally_in_backpressured_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                    .Delay(TimeSpan.FromMilliseconds(300), DelayOverflowStrategy.Backpressure)
                    .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(5)
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(1, TimeSpan.FromMilliseconds(200))
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(2, TimeSpan.FromMilliseconds(200))
                    .ExpectNoMsg(TimeSpan.FromMilliseconds(200))
                    .ExpectNext(3, TimeSpan.FromMilliseconds(200));
            }, Materializer);
        }

        [Fact]
        public void A_Delay_must_fail_on_overflow_in_Fail_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var actualError = Source.From(Enumerable.Range(1, 20))
                    .Delay(TimeSpan.FromMilliseconds(300), DelayOverflowStrategy.Fail)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .RunWith(this.SinkProbe<int>(), Materializer)
                    .Request(100)
                    .ExpectError();

                actualError.Should().BeOfType<BufferOverflowException>();
                actualError.Message.Should().Be("Buffer overflow for Delay combinator (max capacity was: 16)!");
            }, Materializer);
        }

        [Fact(Skip ="Racy")]
        public void A_Delay_must_emit_early_when_buffer_is_full_and_in_EmitEarly_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = this.CreateManualSubscriberProbe<int>();
                var p = this.CreateManualPublisherProbe<int>();

                Source.FromPublisher(p)
                    .Delay(TimeSpan.FromSeconds(10), DelayOverflowStrategy.EmitEarly)
                    .WithAttributes(Attributes.CreateInputBuffer(16, 16))
                    .To(Sink.FromSubscriber(c))
                    .Run(Materializer);
                var cSub = c.ExpectSubscription();
                var pSub = p.ExpectSubscription();
                cSub.Request(20);

                Enumerable.Range(1, 16).ForEach(i => pSub.SendNext(i));
                c.ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                pSub.SendNext(17);
                c.ExpectNext(1, TimeSpan.FromMilliseconds(100));
                // fail will terminate despite of non empty internal buffer
                pSub.SendError(new Exception());
            }, Materializer);
        }

        // Passed 500 consecutive local test runs with no fail with very heavy load without modification
        [Fact(Skip ="Racy")]
        public void A_Delay_must_properly_delay_according_to_buffer_size()
        {
            // With a buffer size of 1, delays add up 
            Source.From(Enumerable.Range(1, 5))
                .Delay(TimeSpan.FromMilliseconds(500), DelayOverflowStrategy.Backpressure)
                .WithAttributes(Attributes.CreateInputBuffer(1, 1))
                .RunWith(Sink.Ignore<int>(), Materializer)
                .PipeTo(TestActor, success: () => Done.Instance);

            ExpectNoMsg(TimeSpan.FromSeconds(2));
            ExpectMsg<Done>();

            // With a buffer large enough to hold all arriving elements, delays don't add up 
            Source.From(Enumerable.Range(1, 100))
                .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                .WithAttributes(Attributes.CreateInputBuffer(100, 100))
                .RunWith(Sink.Ignore<int>(), Materializer)
                .PipeTo(TestActor, success: () => Done.Instance);

            ExpectMsg<Done>();

            // Delays that are already present are preserved when buffer is large enough 
            Source.Tick(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100), NotUsed.Instance)
                .Take(10)
                .Delay(TimeSpan.FromSeconds(1), DelayOverflowStrategy.Backpressure)
                .WithAttributes(Attributes.CreateInputBuffer(10, 10))
                .RunWith(Sink.Ignore<NotUsed>(), Materializer)
                .PipeTo(TestActor, success: () => Done.Instance);

            ExpectNoMsg(TimeSpan.FromMilliseconds(900));
            ExpectMsg<Done>();
        }

        [Fact]
        public void A_Delay_must_not_overflow_buffer_when_DelayOverflowStrategy_is_Backpressure()
        {
            var probe = Source.From(Enumerable.Range(1, 6))
                .Delay(TimeSpan.FromMilliseconds(100), DelayOverflowStrategy.Backpressure)
                .WithAttributes(Attributes.CreateInputBuffer(2, 2))
                .Throttle(1, TimeSpan.FromMilliseconds(200), 1, ThrottleMode.Shaping)
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(10)
                .ExpectNext(1, 2, 3, 4, 5, 6)
                .ExpectComplete();
        }
    }
}

