//-----------------------------------------------------------------------
// <copyright file="FlowDelaySpec.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.Streams.TestKit.Tests;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class FlowDelaySpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public FlowDelaySpec(ITestOutputHelper helper) : base(helper)
        {
            Materializer = ActorMaterializer.Create(Sys);
        }

        [Fact]
        public void A_Delay_must_deliver_elements_with_some_time_shift()
        {
            var task =
                Source.From(Enumerable.Range(1, 10))
                    .Delay(TimeSpan.FromSeconds(1))
                    .Grouped(100)
                    .RunWith(Sink.First<IEnumerable<int>>(), Materializer);
            task.Wait(TimeSpan.FromMilliseconds(1200)).Should().BeTrue();
            task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(1, 10));
        }

        [Fact]
        public void A_Delay_must_add_delay_to_initialDelay_if_exists_upstream()
        {
            var probe = Source.From(Enumerable.Range(1, 10))
                .InitialDelay(TimeSpan.FromSeconds(1))
                .Delay(TimeSpan.FromSeconds(1))
                .RunWith(this.SinkProbe<int>(), Materializer);

            probe.Request(10);
            probe.ExpectNoMsg(TimeSpan.FromMilliseconds(1800));
            probe.ExpectNext(1, TimeSpan.FromMilliseconds(300));
            probe.ExpectNextN(Enumerable.Range(2, 9));
            probe.ExpectComplete();
        }

        [Fact]
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

        [Fact]
        public void A_Delay_must_deliver_elements_with_delay_for_slow_stream()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = TestSubscriber.CreateManualProbe<int>(this);
                var p = TestPublisher.CreateManualProbe<int>(this);

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

                task.Wait(TimeSpan.FromMilliseconds(1200)).Should().BeTrue();
                var expected = Enumerable.Range(1, 15).ToList();
                expected.Add(20);
                task.Result.ShouldAllBeEquivalentTo(expected);
            }, Materializer);
        }

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

                task.Wait(TimeSpan.FromMilliseconds(1200)).Should().BeTrue();
                task.Result.ShouldAllBeEquivalentTo(Enumerable.Range(5, 16));
            }, Materializer);
        }

        [Fact]
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

        [Fact]
        public void A_Delay_must_pass_elements_with_delay_through_normally_in_backpressured_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                Source.From(Enumerable.Range(1, 3))
                .Delay(TimeSpan.FromMilliseconds(300), DelayOverflowStrategy.Backpressure)
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

        [Fact]
        public void A_Delay_must_emit_early_when_buffer_is_full_and_in_EmitEarly_mode()
        {
            this.AssertAllStagesStopped(() =>
            {
                var c = TestSubscriber.CreateManualProbe<int>(this);
                var p = TestPublisher.CreateManualProbe<int>(this);

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
                pSub.SendError(new SystemException());
            }, Materializer);
        }
    }
}
