//-----------------------------------------------------------------------
// <copyright file="CancelableTests.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor.Cancellation
{
    public class CancelableTests : AkkaSpec
    {
        [Fact]
        public void Should_not_be_cancelled_initially()
        {
            var c = new Cancelable(Sys.Scheduler);
            c.IsCancellationRequested.ShouldBeFalse();
            c.Token.IsCancellationRequested.ShouldBeFalse();
        }

        [Fact]
        public void When_cancel_has_been_called_Should_be_cancelled()
        {
            var c = new Cancelable(Sys.Scheduler);
            c.Cancel();
            c.IsCancellationRequested.ShouldBeTrue();
            c.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void Given_a_canceled_Cancelable_with_callback_Then_Cancel_should_be_possible_to_call_again_but_callbacks_should_not_be_called_again()
        {
            var c = new Cancelable(Sys.Scheduler);
            var callbacks = new AtomicCounter(0);
            c.Token.Register(() => callbacks.IncrementAndGet());
            c.Cancel();

            c.Cancel();

            //HACK: Using the fact that when Cancel is called, callbacks are executed synchronously
            callbacks.Current.ShouldBe(1);
        }

        [Fact]
        public void When_CancelTrue_is_called_Then_first_exception_should_prevent_rest_from_being_called()
        {
            var c = new Cancelable(Sys.Scheduler);
            var callbacks = new AtomicCounter(0);
            c.Token.Register(() => { throw new Exception("Something wonderful has happened."); });
            c.Token.Register(() => callbacks.IncrementAndGet());
            c.Token.Register(() => { throw new Exception("Your AKKA is alive!!!"); });
            try
            {
                //First callback should prevent the second one from being called
                c.Cancel(throwOnFirstException: true);
            }
            catch (Exception e)
            {
                if (!e.Message.StartsWith("Your") && !e.Message.StartsWith("Something"))
                    throw new Exception("Invalid exception received: " + e, e);
            }
            //HACK: Using the fact that when Cancel is called, callbacks are executed synchronously
            callbacks.Current.ShouldBe(0);
            c.IsCancellationRequested.ShouldBeTrue();
            c.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void When_CancelFalse_is_called_Then_first_exception_should_not_prevent_rest_from_being_called()
        {
            var c = new Cancelable(Sys.Scheduler);
            var callbacks = new AtomicCounter(0);
            c.Token.Register(() => { throw new Exception("Something wonderful has happened."); });
            c.Token.Register(() => callbacks.IncrementAndGet());
            c.Token.Register(() => { throw new Exception("Your AKKA is alive!!!"); });
            try
            {
                //First callback should prevent the second one from being called
                c.Cancel(throwOnFirstException: false);
            }
            catch (AggregateException aggregateException)
            {
                aggregateException = aggregateException.Flatten();
                foreach (var e in aggregateException.InnerExceptions)
                {
                    if (!e.Message.StartsWith("Your") && !e.Message.StartsWith("Something"))
                        throw new Exception("Invalid exception received: " + e, e);

                }
            }
            //HACK: Using the fact that when Cancel is called, callbacks are executed synchronously
            callbacks.Current.ShouldBe(1);
            c.IsCancellationRequested.ShouldBeTrue();
            c.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void Should_be_possible_to_call_CancelAfterMs()
        {
            var c = new Cancelable(Sys.Scheduler);
            var latch = CreateTestLatch();
            c.Token.Register(() => latch.CountDown());
            c.CancelAfter(50);
            c.IsCancellationRequested.ShouldBeFalse();
            latch.Ready();
            c.IsCancellationRequested.ShouldBeTrue();
            c.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void Should_be_possible_to_call_CancelAfterTimespan()
        {
            var c = new Cancelable(Sys.Scheduler);
            var latch = CreateTestLatch();
            c.Token.Register(() => latch.CountDown());
            c.CancelAfter(TimeSpan.FromMilliseconds(50));
            c.IsCancellationRequested.ShouldBeFalse();
            latch.Ready();
            c.IsCancellationRequested.ShouldBeTrue();
            c.Token.IsCancellationRequested.ShouldBeTrue();
        }

        [Fact]
        public void Given_linked_Cancelable_When_canceling_underlying_Then_linked_should_be_canceled()
        {
            var underlying = new Cancelable(Sys.Scheduler);
            var linked = Cancelable.CreateLinkedCancelable(Sys.Scheduler, underlying);
            var latch = CreateTestLatch();
            linked.Token.Register(() => latch.CountDown());

            underlying.Cancel();
            underlying.IsCancellationRequested.ShouldBeTrue();
            underlying.Token.IsCancellationRequested.ShouldBeTrue();
            latch.Ready();

            linked.IsCancellationRequested.ShouldBeTrue();
            linked.Token.IsCancellationRequested.ShouldBeTrue();
        }
    }
}

