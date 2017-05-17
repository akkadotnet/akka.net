//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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

            Assert.Equal( breaker.Instance.CurrentFailureCount, 0 );
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
        }

        [Fact( DisplayName = "A synchronous circuit breaker that is closed should reset failure count when call succeeds" )]
        public void Should_Reset_FailureCount_When_Call_Succeeds( )
        {
            var breaker = MultiFailureCb( );

            Assert.Equal( breaker.Instance.CurrentFailureCount, 0 );
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.Equal( breaker.Instance.CurrentFailureCount, 1 );

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

        [Fact(DisplayName = "A synchronous circuit breaker that is half open should pass call and transition to open on exception")]
        public void Should_Pass_Call_And_Transition_To_Open_On_Exception( )
        {
            var breaker = ShortResetTimeoutCb( );


            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.HalfOpenLatch ) );

            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithSyncCircuitBreaker( ThrowException ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
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

            Assert.Equal( breaker.Instance.CurrentFailureCount, 0 );
            Assert.True( InterceptExceptionType<TestException>( ( ) => breaker.Instance.WithCircuitBreaker( () => Task.Run( ( ) => ThrowException( ) ) ).Wait( AwaitTimeout ) ) );
            Assert.True( CheckLatch( breaker.OpenLatch ) );
            Assert.Equal( 1, breaker.Instance.CurrentFailureCount );
        }

        [Fact(DisplayName = "An asynchronous circuit breaker that is closed should reset failure count when call succeeds after failure")]
        public void Should_Reset_Failure_Count_When_Call_Succeeds_After_Failure( )
        {
            var breaker = MultiFailureCb( );

            Assert.Equal( breaker.Instance.CurrentFailureCount, 0 );

            var whenall = Task.WhenAll(
                breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException))
                , breaker.Instance.WithCircuitBreaker(() => Task.Factory.StartNew(ThrowException)));

            Assert.True( InterceptExceptionType<TestException>( ( ) => whenall.Wait( AwaitTimeout ) ) );

            Assert.Equal( breaker.Instance.CurrentFailureCount, 4 );

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

        public void ThrowException( )
        {
            throw new TestException( "Test Exception" );
        }

        public string SayTest( )
        {
            return "Test";
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
                var aggregate = ex as AggregateException;
                if ( aggregate != null )
                {

                    // ReSharper disable once UnusedVariable
                    foreach ( var temp in aggregate.InnerExceptions.Select( innerException => innerException as T ).Where( temp => temp == null ) )
                    {
                        throw;
                    }
                }
                else
                {
                    var temp = ex as T;

                    if ( temp == null )
                    {
                        throw;
                    }
                }

            }
            return true;
        }

        public TestBreaker ShortCallTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker( 1, TimeSpan.FromMilliseconds( 50 ), TimeSpan.FromMilliseconds( 500 ) ) );
        }

        public TestBreaker ShortResetTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker( 1, TimeSpan.FromMilliseconds( 1000 ), TimeSpan.FromMilliseconds( 50 ) ) );
        }

        public TestBreaker LongCallTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker( 1, TimeSpan.FromMilliseconds( 5000 ), TimeSpan.FromMilliseconds( 500 ) ) );
        }

        public TestBreaker LongResetTimeoutCb( )
        {
            return new TestBreaker( new CircuitBreaker( 1, TimeSpan.FromMilliseconds( 100 ), TimeSpan.FromMilliseconds( 5000 ) ) );
        }

        public TestBreaker MultiFailureCb( )
        {
            return new TestBreaker( new CircuitBreaker( 5, TimeSpan.FromMilliseconds( 200 ), TimeSpan.FromMilliseconds( 500 ) ) );
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