//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Tests.Pattern
{
    public class ASynchronousCircuitBreakerThatIsClosed : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is closed must allow calls through")]
        public void Must_allow_calls_through()
        {
            var breaker = LongCallTimeoutCb();
            breaker.Instance.WithSyncCircuitBreaker(SayHi).ShouldBe("hi");
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must increment failure count on failure")]
        public void Must_increment_failure_count_on_failure()
        {
            var breaker = LongCallTimeoutCb();
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must increment failure count on fail method")]
        public void Must_increment_failure_count_on_fail_method()
        {
            var breaker = LongCallTimeoutCb();
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
            breaker.Instance.Fail();
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must invoke onOpen if call fails and breaker transits to open state")]
        public void Must_invoke_onOpen_if_call_fails_and_breaker_transits_to_open_state()
        {
            var breaker = ShortCallTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must reset failure count after success")]
        public void Must_reset_failure_count_after_success()
        {
            var breaker = MultiFailureCb();
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
            breaker.Instance.WithSyncCircuitBreaker(SayHi);
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must reset failure count after success method")]
        public void Must_reset_failure_count_after_success_method()
        {
            var breaker = MultiFailureCb();
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
            breaker.Instance.Succeed();
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must increment failure count on callTimeout before call finishes")]
        public void Must_increment_failure_count_on_callTimeout_before_call_finishes()
        {
            var breaker = ShortCallTimeoutCb();
            Task.Run(() => breaker.Instance.WithSyncCircuitBreaker(() => Thread.Sleep(Dilated(TimeSpan.FromSeconds(1)))));
            Within(TimeSpan.FromMilliseconds(900),
                () => AwaitCondition(() => breaker.Instance.CurrentFailureCount == 1, Dilated(TimeSpan.FromMilliseconds(100)), TimeSpan.FromMilliseconds(100)));
        }
    }

    public class ASynchronousCircuitBreakerThatIsHalfOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is half open must pass through next call and close on success")]
        public void Must_pass_through_call_and_close_on_success()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            Assert.True("hi" == breaker.Instance.WithSyncCircuitBreaker(SayHi));
            Assert.True(CheckLatch(breaker.ClosedLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open must open on exception in call")]
        public void Must_open_on_exception_in_call()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.OpenLatch.Reset();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open on calling fail method")]
        public void Must_open_on_calling_fail_method()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.OpenLatch.Reset();
            breaker.Instance.Fail();
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open on calling success method")]
        public void Must_open_on_calling_success_method()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.Instance.Succeed();
            Assert.True(CheckLatch(breaker.ClosedLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open must close on calling success method")]
        public void Must_close_on_calling_success_method()
        {
            var breaker = ShortResetTimeoutCb();

            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.Instance.Succeed();
            Assert.True(CheckLatch(breaker.ClosedLatch));
        }
    }

    public class ASynchronousCircuitBreakerThatIsOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is open must throw exceptions when called before reset timeout")]
        public void Must_throw_exceptions_before_reset_timeout()
        {
            var breaker = LongResetTimeoutCb();

            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));

            var e = InterceptException<OpenCircuitException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            e.RemainingDuration.ShouldBeGreaterThan(TimeSpan.Zero);
            e.RemainingDuration.ShouldBeLessOrEqualTo(LongResetTimeout);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open must transition to half-open on reset timeout")]
        public void Must_transition_to_half_open_on_reset_timeout()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open must still be in open state after calling success method")]
        public void Must_still_be_in_open_state_after_calling_success_method()
        {
            var breaker = LongResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.Succeed();
            breaker.Instance.IsOpen.ShouldBeTrue();
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open must still be in open state after calling fail method")]
        public void Must_still_be_in_open_state_after_calling_fail_method()
        {
            var breaker = LongResetTimeoutCb();
            InterceptException<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.Fail();
            breaker.Instance.IsOpen.ShouldBeTrue();
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsClosed : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must allow calls through")]
        public async Task Must_allow_calls_through()
        {
            var breaker = LongCallTimeoutCb();
            var result = await breaker.Instance.WithCircuitBreaker(() => Task.Run(SayHi)).WaitAsync(AwaitTimeout);
            Assert.Equal("hi", result);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on exception")]
        public async Task Must_increment_failure_count_on_exception()
        {
            var breaker = LongCallTimeoutCb();
            await InterceptException<TestException>(() =>
                breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)).WaitAsync(AwaitTimeout));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on async failure")]
        public void Must_increment_failure_count_on_async_failure()
        {
            var breaker = LongCallTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must reset failure count after success")]
        public async Task Must_reset_failure_count_after_success()
        {
            var breaker = MultiFailureCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(SayHi));
            Enumerable.Range(1, 4).ForEach(_ => breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)));
            await AwaitAssertAsync(() => breaker.Instance.CurrentFailureCount.ShouldBe(4), AwaitTimeout);
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(SayHi));
            await AwaitAssertAsync(() => breaker.Instance.CurrentFailureCount.ShouldBe(0), AwaitTimeout);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on callTimeout")]
        public async Task Must_increment_failure_count_on_callTimeout()
        {
            var breaker = ShortCallTimeoutCb();

            var future = breaker.Instance.WithCircuitBreaker(async () =>
            {
                await Task.Delay(150);
                ThrowException();
            });

            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.CurrentFailureCount.ShouldBe(1);

            // Since the timeout should have happened before the inner code finishes
            // we expect a timeout, not TestException
            await InterceptException<TimeoutException>(() => future.WaitAsync(AwaitTimeout));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must invoke onOpen if call fails and breaker transits to open state")]
        public void Must_invoke_onOpen_if_call_fails_and_breaker_transits_to_open_state()
        {
            var breaker = ShortCallTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsHalfOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must pass through next call and close on success")]
        public async Task Must_pass_through_next_call_and_close_on_success()
        {
            var breaker = ShortResetTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            var result = await breaker.Instance.WithCircuitBreaker(() => Task.Run(SayHi)).WaitAsync(AwaitTimeout);
            Assert.Equal("hi", result);
            Assert.True(CheckLatch(breaker.ClosedLatch));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must re-open on exception in call")]
        public async Task Must_reopen_on_exception_in_call()
        {
            var breaker = ShortResetTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.OpenLatch.Reset();
            await InterceptException<TestException>(() =>
                breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)).WaitAsync(AwaitTimeout));
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must re-open on async failure")]
        public void Must_reopen_on_async_failure()
        {
            var breaker = ShortResetTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));

            breaker.OpenLatch.Reset();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is open must throw exceptions when called before reset timeout")]
        public async Task Must_throw_exceptions_when_called_before_reset_timeout()
        {
            var breaker = LongResetTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));

            Assert.True(CheckLatch(breaker.OpenLatch));

            await InterceptException<OpenCircuitException>(
                () => breaker.Instance.WithCircuitBreaker(() => Task.Run(SayHi)).WaitAsync(AwaitTimeout));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is open must transition to half-open on reset timeout")]
        public void Must_transition_to_half_open_on_reset_timeout()
        {
            var breaker = ShortResetTimeoutCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is open must increase the reset timeout after it transits to open again")]
        public async Task Must_increase_reset_timeout_after_it_transits_to_open_again()
        {
            var breaker = NonOneFactorCb();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));

            var e1 = await InterceptException<OpenCircuitException>(
                () => breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)));
            var shortRemainingDuration = e1.RemainingDuration;

            await Task.Delay(Dilated(TimeSpan.FromMilliseconds(1000)));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));

            // transit to open again
            breaker.OpenLatch.Reset();
            _ = breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException));
            Assert.True(CheckLatch(breaker.OpenLatch));

            var e2 = await InterceptException<OpenCircuitException>(() =>
                breaker.Instance.WithCircuitBreaker(() => Task.FromResult(SayHi())));
            var longRemainingDuration = e2.RemainingDuration;

            shortRemainingDuration.ShouldBeLessThan(longRemainingDuration);
        }
    }

    public class CircuitBreakerSpecBase : AkkaSpec
    {
        public TimeSpan AwaitTimeout => Dilated(TimeSpan.FromSeconds(2));

        public bool CheckLatch(CountdownEvent latch) => latch.Wait(AwaitTimeout);

        [DebuggerStepThrough]
        public static void ThrowException() => throw new TestException("Test Exception");

        public static string SayHi() => "hi";

        protected T InterceptException<T>(Action actionThatThrows) where T : Exception =>
            Intercept<T>(actionThatThrows);

        protected static async Task<T> InterceptException<T>(Func<Task> actionThatThrows)
            where T : Exception
        {
            return (await actionThatThrows.Should().ThrowExactlyAsync<T>()).And;
        }

        public TestBreaker ShortCallTimeoutCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 1, Dilated(TimeSpan.FromMilliseconds(50)), Dilated(TimeSpan.FromMilliseconds(500))));

        public TestBreaker ShortResetTimeoutCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 1, Dilated(TimeSpan.FromMilliseconds(1000)), Dilated(TimeSpan.FromMilliseconds(50))));

        public TestBreaker LongCallTimeoutCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromSeconds(5), Dilated(TimeSpan.FromMilliseconds(500))));

        public TimeSpan LongResetTimeout = TimeSpan.FromSeconds(5);
        public TestBreaker LongResetTimeoutCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 1, Dilated(TimeSpan.FromMilliseconds(100)), Dilated(LongResetTimeout)));

        public TestBreaker MultiFailureCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 5, Dilated(TimeSpan.FromMilliseconds(200)), Dilated(TimeSpan.FromMilliseconds(500))));

        public TestBreaker NonOneFactorCb() =>
            new(new CircuitBreaker(Sys.Scheduler, 1, Dilated(TimeSpan.FromMilliseconds(2000)), Dilated(TimeSpan.FromMilliseconds(1000)), Dilated(TimeSpan.FromDays(1)), 5, 0));
    }

    internal class TestException : Exception
    {
        public TestException()
        {
        }

        public TestException(string message)
            : base(message)
        {
        }

        public TestException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected TestException(SerializationInfo info, StreamingContext context)
            : base(info, context)
        {
        }
    }
}
