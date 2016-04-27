//-----------------------------------------------------------------------
// <copyright file="CircuitBreaker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Util.Internal;

namespace Akka.Pattern
{
    /// <summary>
    /// Provides circuit breaker functionality to provide stability when working with 
    /// "dangerous" operations, e.g. calls to remote systems
    /// 
    ///<list type="bullet">
    ///<listheader>
    ///    <description>Transitions through three states:</description>
    ///</listheader>
    ///<item>
    ///    <term>In *Closed* state, </term>
    ///    <description>calls pass through until the maxFailures count is reached. 
    ///         This causes the circuit breaker to open. Both exceptions and calls exceeding 
    ///         callTimeout are considered failures.</description>
    ///</item>
    ///<item>
    ///    <term>In *Open* state, </term>
    ///    <description>calls fail-fast with an exception. After resetTimeout, 
    ///         circuit breaker transitions to half-open state.</description>
    ///</item>
    ///<item>
    ///    <term>In *Half-Open* state, </term>
    ///    <description>the first call will be allowed through, if it succeeds 
    ///         the circuit breaker will reset to closed state. If it fails, the circuit 
    ///         breaker will re-open to open state. All calls beyond the first that execute 
    ///         while the first is running will fail-fast with an exception.</description>
    ///</item>
    ///</list>
    /// </summary>
    public class CircuitBreaker
    {
        /// <summary>
        /// The current state of the breaker -- Closed, Half-Open, or Closed -- *access only via helper methods*
        /// </summary>
        private AtomicState _currentState;

        /// <summary>
        /// Helper method for access to the underlying state via Interlocked
        /// </summary>
        /// <param name="oldState">Previous state on transition</param>
        /// <param name="newState">Next state on transition</param>
        /// <returns>Whether the previous state matched correctly</returns>
        private bool SwapState( AtomicState oldState, AtomicState newState )
        {
            return Interlocked.CompareExchange( ref _currentState, newState, oldState ) == oldState;
        }

        /// <summary>
        /// Helper method for access to the underlying state via Interlocked
        /// </summary>
        private AtomicState CurrentState
        {
            get
            {
                Interlocked.MemoryBarrier( );
                return _currentState;
            }
        }

        public int MaxFailures { get; private set; }

        public TimeSpan CallTimeout { get; private set; }
        public TimeSpan ResetTimeout { get; private set; }
        
        //akka.io implementation is to use nested static classes and access parent member variables
        //.Net static nested classes do not have access to parent member variables -- so we configure the states here and
        //swap them above
        private AtomicState Closed { get; set; } 
        private AtomicState Open { get; set; }
        private AtomicState HalfOpen { get; set; }

        /// <summary>
        /// Create a new CircuitBreaker
        /// </summary>
        /// <param name="maxFailures">Maximum number of failures before opening the circuit</param>
        /// <param name="callTimeout"><see cref="TimeSpan"/> of time after which to consider a call a failure</param>
        /// <param name="resetTimeout"><see cref="TimeSpan"/> of time after which to attempt to close the circuit</param>
        /// <returns></returns>
        public static CircuitBreaker Create( int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout )
        {
            return new CircuitBreaker( maxFailures, callTimeout, resetTimeout );
        }

        /// <summary>
        /// Create a new CircuitBreaker
        /// </summary>
        /// <param name="maxFailures">Maximum number of failures before opening the circuit</param>
        /// <param name="callTimeout"><see cref="TimeSpan"/> of time after which to consider a call a failure</param>
        /// <param name="resetTimeout"><see cref="TimeSpan"/> of time after which to attempt to close the circuit</param>
        /// <returns></returns>
        public CircuitBreaker( int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout )
        {
            MaxFailures = maxFailures;
            CallTimeout = callTimeout;
            ResetTimeout = resetTimeout;
            Closed = new Closed( this );
            Open = new Open( this );
            HalfOpen = new HalfOpen( this );
            _currentState = Closed;
            //_failures = new AtomicInteger();
        }

        /// <summary>
        /// Retrieves current failure count.
        /// </summary>
        public long CurrentFailureCount
        {
            get { return Closed.Current; }
        }

        /// <summary>
        /// Wraps invocation of asynchronous calls that need to be protected
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="body">Call needing protected</param>
        /// <returns><see cref="Task"/> containing the call result</returns>
        public async Task<T> WithCircuitBreaker<T>( Func<Task<T>> body )
        {
            return await CurrentState.Invoke<T>( body );
        }

        /// <summary>
        /// Wraps invocation of asynchronous calls that need to be protected
        /// </summary>
        /// <param name="body">Call needing protected</param>
        /// <returns><see cref="Task"/></returns>
        public async Task WithCircuitBreaker( Func<Task> body )
        {
            await CurrentState.Invoke( body );
        }

        /// <summary>
        /// The failure will be recorded farther down.
        /// </summary>
        /// <param name="body"></param>
        public void WithSyncCircuitBreaker( Action body )
        {
            var cbTask = WithCircuitBreaker( () => Task.Factory.StartNew( body ) );
            if ( !cbTask.Wait( CallTimeout ) )
            {
                //throw new TimeoutException( string.Format( "Execution did not complete within the time alotted {0} ms", CallTimeout.TotalMilliseconds ) );
            }
            if ( cbTask.Exception != null )
            {
                throw cbTask.Exception;
            }
        }

        /// <summary>
        /// Wraps invocations of asynchronous calls that need to be protected
        /// If this does not complete within the time allotted, it should return default(<typeparamref name="T"/>)
        /// 
        /// <code>
        ///  Await.result(
        ///      withCircuitBreaker(try Future.successful(body) catch { case NonFatal(t) ⇒ Future.failed(t) }),
        ///      callTimeout)
        /// </code>
        ///
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="body"></param>
        /// <returns><typeparamref name="T"/> or default(<typeparamref name="T"/>)</returns>
        public T WithSyncCircuitBreaker<T>( Func<T> body )
        {
            var cbTask = WithCircuitBreaker( () => Task.Factory.StartNew( body ) );
            return cbTask.Wait( CallTimeout ) ? cbTask.Result : default(T);
        }

        /// <summary>
        /// Adds a callback to execute when circuit breaker opens
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnOpen( Action callback )
        {
            Open.AddListener( callback );
            return this;
        }

        /// <summary>
        /// Adds a callback to execute when circuit breaker transitions to half-open
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnHalfOpen( Action callback )
        {
            HalfOpen.AddListener( callback );
            return this;
        }

        /// <summary>
        /// Adds a callback to execute when circuit breaker state closes
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnClose( Action callback )
        {
            Closed.AddListener( callback );
            return this;
        }

        /// <summary>
        /// Implements consistent transition between states. Throws IllegalStateException if an invalid transition is attempted.
        /// </summary>
        /// <param name="fromState">State being transitioning from</param>
        /// <param name="toState">State being transitioned to</param>
        private void Transition( AtomicState fromState, AtomicState toState )
        {
            if ( SwapState( fromState, toState ) )
            {
                Debug.WriteLine( "Successful transition from {0} to {1}", fromState, toState );
                toState.Enter( );
            }
            else
            {
                throw new IllegalStateException( string.Format( "Illegal transition attempted from {0} to {1}", fromState, toState ) );
            }
        }

        /// <summary>
        /// Trips breaker to an open state. This is valid from Closed or Half-Open states
        /// </summary>
        /// <param name="fromState">State we're coming from (Closed or Half-Open)</param>
        internal void TripBreaker( AtomicState fromState )
        {
            Transition( fromState, Open );
        }

        /// <summary>
        /// Resets breaker to a closed state.  This is valid from an Half-Open state only.
        /// </summary>
        internal void ResetBreaker( )
        {
            Transition( HalfOpen, Closed );
        }

        /// <summary>
        /// Attempts to reset breaker by transitioning to a half-open state.  This is valid from an Open state only.
        /// </summary>
        internal void AttemptReset( )
        {
            Transition( Open, HalfOpen );
        }

        //private readonly Task timeoutTask = Task.FromResult(new TimeoutException("Circuit Breaker Timed out."));
    }
}
