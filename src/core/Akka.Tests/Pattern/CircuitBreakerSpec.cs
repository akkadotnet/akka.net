//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using static FluentAssertions.FluentActions;

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
        public async Task Must_increment_failure_count_on_callTimeout_before_call_finishes()
        {
            var breaker = ShortCallTimeoutCb();
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // meant to run as detached task
            Task.Run(() => breaker.Instance.WithSyncCircuitBreaker(() => Thread.Sleep(Dilated(TimeSpan.FromSeconds(1)))));
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            var epsilon = TimeSpan.FromMilliseconds(500); // need to pad timeouts due to non-determinism of OS scheduler
            await WithinAsync(TimeSpan.FromMilliseconds(900) + epsilon,
                () => AwaitConditionAsync(() => breaker.Instance.CurrentFailureCount == 1, Dilated(TimeSpan.FromMilliseconds(100)), TimeSpan.FromMilliseconds(100)));
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
            var result = await breaker.Instance.WithCircuitBreaker(SayHiAsync).WaitAsync(AwaitTimeout);
            result.Should().Be("hi");
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on exception")]
        public async Task Must_increment_failure_count_on_exception()
        {
            var breaker = LongCallTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));

            CheckLatch(breaker.OpenLatch).Should().BeTrue();
            breaker.Instance.CurrentFailureCount.Should().Be(1);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on async failure")]
        public async Task Must_increment_failure_count_on_async_failure()
        {
            var breaker = LongCallTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));

            CheckLatch(breaker.OpenLatch).Should().BeTrue();
            breaker.Instance.CurrentFailureCount.Should().Be(1);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must reset failure count after success")]
        public async Task Must_reset_failure_count_after_success()
        {
            var breaker = MultiFailureCb();
            _ = await breaker.Instance.WithCircuitBreaker(SayHiAsync).WaitAsync(AwaitTimeout);

            await Task.WhenAll(Enumerable.Range(1, 4)
                .Select(_ 
                    => InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync))));

            breaker.Instance.CurrentFailureCount.Should().Be(4);
            
            var result = await breaker.Instance.WithCircuitBreaker(SayHiAsync).WaitAsync(AwaitTimeout);
            result.Should().Be("hi");
            breaker.Instance.CurrentFailureCount.ShouldBe(0);
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must increment failure count on callTimeout")]
        public async Task Must_increment_failure_count_on_callTimeout()
        {
            var breaker = ShortCallTimeoutCb();

            Task innerFuture = null;
            var future = breaker.Instance.WithCircuitBreaker(ct =>
            {
                innerFuture = SlowThrowing(ct);
                return innerFuture;
            });

            CheckLatch(breaker.OpenLatch).Should().BeTrue();
            breaker.Instance.CurrentFailureCount.Should().Be(1);

            // Since the timeout should have happened before the inner code finishes
            // we expect a timeout, not TestException
            await InterceptException<TimeoutException>(() => future);
            
            // Issue https://github.com/akkadotnet/akka.net/issues/7358
            // In the bug, TestException was thrown out-of-band with no handler because inner Task is detached
            // after a timeout and NOT protected when it SHOULD be cancelled/stopped.
            innerFuture.IsCompleted.Should().BeTrue();
            innerFuture.IsCanceled.Should().BeTrue();
            
            return;
            
            async Task SlowThrowing(CancellationToken ct)
            {
                await Task.Delay(500, ct);
                await ThrowExceptionAsync(ct);
            }
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed must invoke onOpen if call fails and breaker transits to open state")]
        public async Task Must_invoke_onOpen_if_call_fails_and_breaker_transits_to_open_state()
        {
            var breaker = ShortCallTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsHalfOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must pass through next call and close on success")]
        public async Task Must_pass_through_next_call_and_close_on_success()
        {
            var breaker = ShortResetTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.HalfOpenLatch).Should().BeTrue();

            var result = await breaker.Instance.WithCircuitBreaker(SayHiAsync).WaitAsync(AwaitTimeout);
            result.Should().Be("hi");
            CheckLatch(breaker.ClosedLatch).Should().BeTrue();
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must re-open on exception in call")]
        public async Task Must_reopen_on_exception_in_call()
        {
            var breaker = ShortResetTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.HalfOpenLatch).Should().BeTrue();
            
            breaker.OpenLatch.Reset();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open must re-open on async failure")]
        public async Task Must_reopen_on_async_failure()
        {
            var breaker = ShortResetTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.HalfOpenLatch).Should().BeTrue();

            breaker.OpenLatch.Reset();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is open must throw exceptions when called before reset timeout")]
        public async Task Must_throw_exceptions_when_called_before_reset_timeout()
        {
            var breaker = LongResetTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();

            await InterceptException<OpenCircuitException>(() => breaker.Instance.WithCircuitBreaker(SayHiAsync));
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is open must transition to half-open on reset timeout")]
        public async Task Must_transition_to_half_open_on_reset_timeout()
        {
            var breaker = ShortResetTimeoutCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.HalfOpenLatch).Should().BeTrue();
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is open must increase the reset timeout after it transits to open again")]
        public async Task Must_increase_reset_timeout_after_it_transits_to_open_again()
        {
            var breaker = NonOneFactorCb();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            _ = breaker.Instance.WithCircuitBreaker(ct => Task.Run(ThrowException, ct));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();

            var e1 = await InterceptException<OpenCircuitException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            var shortRemainingDuration = e1.RemainingDuration;

            await Task.Delay(Dilated(TimeSpan.FromMilliseconds(1000)));
            CheckLatch(breaker.HalfOpenLatch).Should().BeTrue();

            // transit to open again
            breaker.OpenLatch.Reset();
            await InterceptException<TestException>(() => breaker.Instance.WithCircuitBreaker(ThrowExceptionAsync));
            CheckLatch(breaker.OpenLatch).Should().BeTrue();

            var e2 = await InterceptException<OpenCircuitException>(() => breaker.Instance.WithCircuitBreaker(SayHiAsync));
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

        [DebuggerStepThrough]
        public static async Task ThrowExceptionAsync(CancellationToken ct)
        {
            await Task.Yield();
            throw new TestException("Test Exception");
        }

        public static string SayHi() => "hi";

        public static async Task<string> SayHiAsync(CancellationToken ct)
        {
            await Task.Yield();
            return "hi";
        }

        protected static T InterceptException<T>(Action actionThatThrows) where T : Exception =>
            actionThatThrows.Should().Throw<T>().And;

        protected async Task<T> InterceptException<T>(Func<Task> actionThatThrows)
            where T : Exception
        {
            return (await Awaiting(() => actionThatThrows().WaitAsync(AwaitTimeout))
                .Should().ThrowExactlyAsync<T>()).And;
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
