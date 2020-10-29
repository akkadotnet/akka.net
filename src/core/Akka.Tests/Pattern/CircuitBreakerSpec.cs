//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Akka.Pattern;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Pattern
{
    public class ASynchronousCircuitBreakerThatIsClosed : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is closed should allow call through")]
        public void Should_Allow_Call_Through( )
        {
            var breaker = LongCallTimeoutCb( );
            var result = breaker.Instance.WithSyncCircuitBreaker( ( ) => "Test" );

            Assert.Equal( "Test", result );
        }

        [Fact( DisplayName = "A synchronous circuit breaker that is closed should increment failure count when call fails" )]
        public void Should_Increment_FailureCount_When_Call_Fails( )
        {
            var breaker = LongCallTimeoutCb( );

            Assert.Equal(0, breaker.Instance.CurrentFailureCount);
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
        }

        [Fact( DisplayName = "A synchronous circuit breaker that is closed should reset failure count when call succeeds" )]
        public void Should_Reset_FailureCount_When_Call_Succeeds( )
        {
            var breaker = MultiFailureCb( );

            Assert.Equal(0, breaker.Instance.CurrentFailureCount);
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.Equal(1, breaker.Instance.CurrentFailureCount);

            breaker.Instance.WithSyncCircuitBreaker( ( ) => "Test" );

            Assert.Equal( 0, breaker.Instance.CurrentFailureCount );
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed should increment failure count when call times out")]
        public void Should_Increment_FailureCount_When_Call_Times_Out( )
        {
            var breaker = ShortCallTimeoutCb( );

            breaker.Instance.WithSyncCircuitBreaker( ( ) => Thread.Sleep( 500 ) );

            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must increment failure count on fail method")]
        public void Must_increment_failure_count_on_fail_method()
        {
            var breaker = LongCallTimeoutCb();
            Assert.True(breaker.Instance.CurrentFailureCount == 0);
            breaker.Instance.Fail();
            Assert.True(CheckLatch(breaker.OpenLatch));
            Assert.True(breaker.Instance.CurrentFailureCount == 1);
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is closed must reset failure count and clears cached last exception after success method")]
        public void Must_reset_failure_count_after_success_method()
        {
            var breaker = MultiFailureCb();
            Assert.True(breaker.Instance.CurrentFailureCount == 0);
            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException)));
            Assert.True(breaker.Instance.CurrentFailureCount == 1);
            Assert.True(breaker.Instance.LastCaughtException is TestException);
            breaker.Instance.Succeed();
            Assert.True(breaker.Instance.CurrentFailureCount == 0);
            Assert.True(breaker.Instance.LastCaughtException is null);
        }
    }

    public class ASynchronousCircuitBreakerThatIsHalfOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is half open should pass call and transition to close on success")]
        public void Should_Pass_Call_And_Transition_To_Close_On_Success( )
        {
            var breaker = ShortResetTimeoutCb( );
            InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            var result = breaker.Instance.WithSyncCircuitBreaker( ( ) => SayTest( ) );

            Assert.True( CheckLatch( breaker.ClosedLatch ) );
            Assert.Equal( SayTest( ), result );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open should pass only one call until it closes")]
        public async Task Should_Pass_Only_One_Call_And_Transition_To_Close_On_Success()
        {
            var breaker = ShortResetTimeoutCb();
            InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));

            var task1 = breaker.Instance.WithCircuitBreaker(() => DelayedSayTest(TimeSpan.FromSeconds(0.1)));
            var task2 = breaker.Instance.WithCircuitBreaker(() => DelayedSayTest(TimeSpan.FromSeconds(0.1)));
            var combined = Task.WhenAny(task1, task2).Unwrap();

            // One of the 2 tasks will throw, because the circuit breaker is half open
            Exception caughtException = null;
            try
            {
                await combined;
            }
            catch (Exception e)
            {
                caughtException = e;
            }
            Assert.True(caughtException is OpenCircuitException);
            Assert.StartsWith("Circuit breaker is half open", caughtException.Message);

            // Wait until one of task completes
            await Task.Delay(TimeSpan.FromSeconds(0.25));
            Assert.True(CheckLatch(breaker.ClosedLatch));

            // We don't know which one of the task got faulted
            string result = null;
            if (task1.IsCompleted && !task1.IsFaulted)
                result = task1.Result;
            else if (task2.IsCompleted && !task2.IsFaulted)
                result = task2.Result;

            Assert.Equal(SayTest(), result);
        }


        [Fact(DisplayName = "A synchronous circuit breaker that is half open should pass call and transition to open on exception")]
        public void Should_Pass_Call_And_Transition_To_Open_On_Exception( )
        {
            var breaker = ShortResetTimeoutCb( );


            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open must open on calling fail method")]
        public void Must_open_on_calling_fail_method()
        {
            var breaker = ShortCallTimeoutCb();

            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException)));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.Instance.Fail();
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is half open must close on calling success method")]
        public void Must_close_on_calling_success_method()
        {
            var breaker = ShortCallTimeoutCb();

            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException)));
            Assert.True(CheckLatch(breaker.HalfOpenLatch));
            breaker.Instance.Succeed();
            Assert.True(CheckLatch(breaker.ClosedLatch));
        }
    }

    public class ASynchronousCircuitBreakerThatIsOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "A synchronous circuit breaker that is open should throw exceptions before reset timeout")]
        public void Should_Throw_Exceptions_Before_Reset_Timeout( )
        {
            var breaker = LongResetTimeoutCb( );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.True( InterceptExceptionType<OpenCircuitException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open should transition to half open when reset times out")]
        public void Should_Transition_To_Half_Open_When_Reset_Times_Out( )
        {
            var breaker = ShortResetTimeoutCb( );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open must still be in open state after calling success method")]
        public void Must_still_be_in_open_state_after_calling_success_method()
        {
            var breaker = LongCallTimeoutCb();

            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException)));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.Succeed();
            Assert.True(CheckLatch(breaker.OpenLatch));
        }

        [Fact(DisplayName = "A synchronous circuit breaker that is open must still be in open state after calling fail method")]
        public void Must_still_be_in_open_state_after_calling_fail_method()
        {
            var breaker = LongCallTimeoutCb();

            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithSyncCircuitBreaker(ThrowException)));
            Assert.True(CheckLatch(breaker.OpenLatch));
            breaker.Instance.Fail();
            Assert.True(CheckLatch(breaker.OpenLatch));
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsClosed : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is closed should allow call through")]
        public void Should_Allow_Call_Through( )
        {
            var breaker = LongCallTimeoutCb( );
            var result = breaker.Instance.WithCircuitBreaker( () => Task.Run( ( ) => SayTest( ) ) );

            Assert.Equal( SayTest( ), result.Result );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed should increment failure count when call fails")]
        public void Should_Increment_Failure_Count_When_Call_Fails( )
        {
            var breaker = LongCallTimeoutCb( );

            Assert.Equal(0, breaker.Instance.CurrentFailureCount);
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Run( ( ) => ThrowException( ) ) ).Wait( AwaitTimeout ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed should reset failure count when call succeeds after failure")]
        public void Should_Reset_Failure_Count_When_Call_Succeeds_After_Failure( )
        {
            var breaker = MultiFailureCb( );

            Assert.Equal(0, breaker.Instance.CurrentFailureCount);

            var whenall = Task.WhenAll(
                breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException)));

            Assert.True( InterceptExceptionType<TestException>( ( ) => whenall.Wait( AwaitTimeout ) ) );

            Assert.Equal(4, breaker.Instance.CurrentFailureCount);

            var result = breaker.Instance.WithCircuitBreaker(() => Task.Run( ( ) => SayTest( ) ) ).Result;

            Assert.Equal( SayTest( ), result );
            Assert.Equal( 0, breaker.Instance.CurrentFailureCount );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed should increment failure count when call times out")]
        public void Should_Increment_Failure_Count_When_Call_Times_Out( )
        {
            var breaker = ShortCallTimeoutCb( );

            breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ( ) =>
            {
                Thread.Sleep( 500 );
                return SayTest( );
            } ) );

            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
            Assert.True(breaker.Instance.LastCaughtException is TimeoutException);
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsHalfOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is half open should pass call and transition to close on success")]
        public void Should_Pass_Call_And_Transition_To_Close_On_Success( )
        {
            var breaker = ShortResetTimeoutCb( );
            InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            var result = breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ( ) => SayTest( ) ) );

            Assert.True( CheckLatch( breaker.ClosedLatch ) );
            Assert.Equal( SayTest( ), result.Result );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open should pass call and transition to open on exception")]
        public void Should_Pass_Call_And_Transition_To_Open_On_Exception( )
        {
            var breaker = ShortResetTimeoutCb( );


            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ).Wait( ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ).Wait( ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is half open should pass call and transition to open on async failure")]
        public void Should_Pass_Call_And_Transition_To_Open_On_Async_Failure( )
        {
            var breaker = ShortResetTimeoutCb( );

            breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
        }
    }

    public class AnAsynchronousCircuitBreakerThatIsOpen : CircuitBreakerSpecBase
    {
        [Fact(DisplayName = "An asynchronous circuit breaker that is open should throw exceptions when called before reset timeout")]
        public void Should_Throw_Exceptions_When_Called_Before_Reset_Timeout( )
        {
            var breaker = LongResetTimeoutCb( );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ).Wait( ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.True( InterceptExceptionType<OpenCircuitException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ).Wait( ) ) );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is open should transition to half open when reset timeout")]
        public void Should_Transition_To_Half_Open_When_Reset_Timeout( )
        {
            var breaker = ShortResetTimeoutCb( );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Factory.StartNew( ThrowException ) ).Wait( ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );
        }
        
        [Fact(DisplayName = "An asynchronous circuit breaker that is open should increase the reset timeout after it transits to open again")]
        public void Should_Reset_Timeout_After_It_Transits_To_Open_Again()
        {
            var breaker = NonOneFactorCb();
            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)).Wait()));
            Assert.True(CheckLatch(breaker.OpenLatch));

            var e1 = InterceptException<OpenCircuitException>(() => breaker.Instance.WithSyncCircuitBreaker(SayTest));
            var shortRemainingDuration = e1.RemainingDuration;

            Thread.Sleep(1000);
            Assert.True(CheckLatch(breaker.HalfOpenLatch));

            // transit to open again
            Assert.True(InterceptExceptionType<TestException>(() => breaker.Instance.WithCircuitBreaker(() => Task.Run(ThrowException)).Wait()));
            Assert.True(CheckLatch(breaker.OpenLatch));

            var e2 = InterceptException<OpenCircuitException>(() => breaker.Instance.WithSyncCircuitBreaker(SayTest));
            var longRemainingDuration = e2.RemainingDuration;

            Assert.True(shortRemainingDuration < longRemainingDuration);
        }
    }

    public class CircuitBreakerSpecBase : AkkaSpec
    {
        private readonly TimeSpan _awaitTimeout = TimeSpan.FromSeconds(2);
        public TimeSpan AwaitTimeout { get { return _awaitTimeout; } }

        public bool CheckLatch( CountdownEvent latch )
        {
            return latch.Wait( AwaitTimeout );
        }

        public Task Delay( TimeSpan toDelay, CancellationToken? token )
        {
            return token.HasValue ? Task.Delay( toDelay, token.Value ) : Task.Delay( toDelay );
        }

        public async Task<string> DelayedSayTest(TimeSpan delay)
        {
            await Task.Delay(delay);
            return "Test";
        }

        [DebuggerStepThrough]
        public void ThrowException() => throw new TestException("Test Exception");

        public string SayTest( ) => "Test";
        
        protected T InterceptException<T>(Action actionThatThrows) where T : Exception
        {
            return Assert.Throws<T>(() =>
            {
                try
                {
                    actionThatThrows();
                }
                catch (AggregateException ex)
                {
                    foreach (var e in ex.Flatten().InnerExceptions.Where(e => e is T).Select(e => e))
                        throw e;                     
                }
            });
        }

        [SuppressMessage( "Microsoft.Design", "CA1004:GenericMethodsShouldProvideTypeParameter" )]
        public bool InterceptExceptionType<T>( Action action ) where T : Exception
        {
            try
            {
                action.Invoke( );
                return false;
            }
            catch ( Exception ex )
            {
                if (ex is AggregateException aggregate)
                {
                    // ReSharper disable once UnusedVariable
                    foreach (var temp in aggregate
                        .InnerExceptions
                        .Where(t => !(t is T)))
                    {
                        throw;
                    }
                } else if (!(ex is T))
                {
                    throw;
                }
            }
            return true;
        }

        public async Task<bool> InterceptExceptionTypeAsync<T>(Task action) where T : Exception
        {
            try
            {
                await action;
                return false;
            }
            catch (Exception ex)
            {
                if (ex is AggregateException aggregate)
                {
                    // ReSharper disable once UnusedVariable
                    foreach (var temp in aggregate
                        .InnerExceptions
                        .Where(t => !(t is T)))
                    {
                        throw;
                    }
                }
                else if (!(ex is T))
                {
                    throw;
                }
            }
            return true;
        }

        public TestBreaker ShortCallTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromMilliseconds( 50 ), TimeSpan.FromMilliseconds( 500 ) ) );
        }

        public TestBreaker ShortResetTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromMilliseconds( 1000 ), TimeSpan.FromMilliseconds( 50 ) ) );
        }

        public TestBreaker LongCallTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromMilliseconds( 5000 ), TimeSpan.FromMilliseconds( 500 ) ) );
        }

        public TestBreaker LongResetTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromMilliseconds( 100 ), TimeSpan.FromMilliseconds( 5000 ) ) );
        }

        public TestBreaker MultiFailureCb( )
        {
            return new TestBreaker( new CircuitBreaker(Sys.Scheduler, 5, TimeSpan.FromMilliseconds( 200 ), TimeSpan.FromMilliseconds( 500 ) ) );
        }
        
        public TestBreaker NonOneFactorCb()
        {
            return new TestBreaker(new CircuitBreaker(Sys.Scheduler, 1, TimeSpan.FromMilliseconds(2000), TimeSpan.FromMilliseconds(1000), TimeSpan.FromDays(1), 5));
        }
    }


    internal class TestException : Exception
    {
        public TestException( )
        {
        }

        public TestException( string message )
            : base( message )
        {
        }

        public TestException( string message, Exception innerException )
            : base( message, innerException )
        {
        }

#if SERIALIZATION
        protected TestException( SerializationInfo info, StreamingContext context )
            : base( info, context )
        {
        }
#endif
    }

}
