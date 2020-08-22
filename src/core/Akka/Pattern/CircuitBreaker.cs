//-----------------------------------------------------------------------
// <copyright file="CircuitBreaker.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
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
        /// Holds reference to current resetTimeout of CircuitBreaker - *access only via helper methods*
        /// </summary>
        private long _currentResetTimeout;

        /// <summary>
        /// Helper method for access to the underlying state via Interlocked
        /// </summary>
        /// <param name="oldState">Previous state on transition</param>
        /// <param name="newState">Next state on transition</param>
        /// <returns>Whether the previous state matched correctly</returns>
        private bool SwapState(AtomicState oldState, AtomicState newState)
        {
            return Interlocked.CompareExchange(ref _currentState, newState, oldState) == oldState;
        }

        /// <summary>
        /// Helper method for access to the underlying state via Interlocked
        /// </summary>
        private AtomicState CurrentState
        {
            get
            {
                Interlocked.MemoryBarrier();
                return _currentState;
            }
        }
        
        /// <summary>
        /// Helper method for updating the underlying resetTimeout via Interlocked
        /// </summary>
        internal bool SwapStateResetTimeout(TimeSpan oldResetTimeout, TimeSpan newResetTimeout)
        {
            return Interlocked.CompareExchange(ref _currentResetTimeout, newResetTimeout.Ticks, oldResetTimeout.Ticks) == oldResetTimeout.Ticks;
        }
        
        /// <summary>
        /// Helper method for access to the underlying resetTimeout via Interlocked
        /// </summary>
        internal TimeSpan CurrentResetTimeout
        {
            get
            {
                Interlocked.MemoryBarrier();
                return TimeSpan.FromTicks(_currentResetTimeout);
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public IScheduler Scheduler { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public int MaxFailures { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan CallTimeout { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan ResetTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan MaxResetTimeout { get; }

        /// <summary>
        /// TBD
        /// </summary>
        public double ExponentialBackoffFactor { get; }

        //akka.io implementation is to use nested static classes and access parent member variables
        //.Net static nested classes do not have access to parent member variables -- so we configure the states here and
        //swap them above
        private AtomicState Closed { get; set; }
        private AtomicState Open { get; set; }
        private AtomicState HalfOpen { get; set; }

        /// <summary>
        /// Create a new CircuitBreaker
        /// </summary>
        /// <param name="scheduler">Reference to Akka scheduler</param>
        /// <param name="maxFailures">Maximum number of failures before opening the circuit</param>
        /// <param name="callTimeout"><see cref="TimeSpan"/> of time after which to consider a call a failure</param>
        /// <param name="resetTimeout"><see cref="TimeSpan"/> of time after which to attempt to close the circuit</param>
        /// <returns>TBD</returns>
        public static CircuitBreaker Create(IScheduler scheduler, int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout)
        {
            return new CircuitBreaker(scheduler, maxFailures, callTimeout, resetTimeout);
        }
        
        /// <summary>
        /// Create a new CircuitBreaker
        /// </summary>
        /// <param name="scheduler">Reference to Akka scheduler</param>
        /// <param name="maxFailures">Maximum number of failures before opening the circuit</param>
        /// <param name="callTimeout"><see cref="TimeSpan"/> of time after which to consider a call a failure</param>
        /// <param name="resetTimeout"><see cref="TimeSpan"/> of time after which to attempt to close the circuit</param>
        /// <returns>TBD</returns>
        public CircuitBreaker(IScheduler scheduler, int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout)
            : this(scheduler, maxFailures, callTimeout, resetTimeout, TimeSpan.FromDays(36500), 1.0)
        {
        }

        /// <summary>
        /// Create a new CircuitBreaker
        /// </summary>
        /// <param name="scheduler">Reference to Akka scheduler</param>
        /// <param name="maxFailures">Maximum number of failures before opening the circuit</param>
        /// <param name="callTimeout"><see cref="TimeSpan"/> of time after which to consider a call a failure</param>
        /// <param name="resetTimeout"><see cref="TimeSpan"/> of time after which to attempt to close the circuit</param>
        /// <param name="maxResetTimeout"></param>
        /// <param name="exponentialBackoffFactor"></param>
        /// <returns>TBD</returns>
        public CircuitBreaker(IScheduler scheduler, int maxFailures, TimeSpan callTimeout, TimeSpan resetTimeout, TimeSpan maxResetTimeout, double exponentialBackoffFactor)
        {
            if (exponentialBackoffFactor < 1.0) throw new ArgumentException("factor must be >= 1.0", nameof(exponentialBackoffFactor));

            Scheduler = scheduler;
            MaxFailures = maxFailures;
            CallTimeout = callTimeout;
            ResetTimeout = resetTimeout;
            MaxResetTimeout = maxResetTimeout;
            ExponentialBackoffFactor = exponentialBackoffFactor;
            Closed = new Closed(this);
            Open = new Open(this);
            HalfOpen = new HalfOpen(this);
            _currentState = Closed;
            _currentResetTimeout = resetTimeout.Ticks;
        }

        /// <summary>
        /// Retrieves current failure count.
        /// </summary>
        public long CurrentFailureCount
        {
            get { return Closed.Current; }
        }

        public Exception LastCaughtException { get; private set; }


        /// <summary>
        /// Wraps invocation of asynchronous calls that need to be protected
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">Call needing protected</param>
        /// <returns><see cref="Task"/> containing the call result</returns>
        public Task<T> WithCircuitBreaker<T>(Func<Task<T>> body)
        {
            return CurrentState.Invoke(body);
        }

        /// <summary>
        /// Wraps invocation of asynchronous calls that need to be protected
        /// </summary>
        /// <param name="body">Call needing protected</param>
        /// <returns><see cref="Task"/></returns>
        public Task WithCircuitBreaker(Func<Task> body)
        {
            return CurrentState.Invoke(body);
        }

        /// <summary>
        /// The failure will be recorded farther down.
        /// </summary>
        /// <param name="body">TBD</param>
        public void WithSyncCircuitBreaker(Action body)
        {
            var cbTask = WithCircuitBreaker(() => Task.Factory.StartNew(body));
            if (!cbTask.Wait(CallTimeout))
            {
                //throw new TimeoutException( string.Format( "Execution did not complete within the time allotted {0} ms", CallTimeout.TotalMilliseconds ) );
            }
            if (cbTask.Exception != null)
            {
                ExceptionDispatchInfo.Capture(cbTask.Exception).Throw();
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
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="body">TBD</param>
        /// <returns><typeparamref name="T"/> or default(<typeparamref name="T"/>)</returns>
        public T WithSyncCircuitBreaker<T>(Func<T> body)
        {
            var cbTask = WithCircuitBreaker(() => Task.Factory.StartNew(body));
            return cbTask.Wait(CallTimeout) ? cbTask.Result : default(T);
        }

        /// <summary>
        /// Mark a successful call through CircuitBreaker. Sometimes the callee of CircuitBreaker sends back a message to the
        /// caller Actor. In such a case, it is convenient to mark a successful call instead of using Future
        /// via <see cref="WithCircuitBreaker"/>
        /// </summary>
        public void Succeed() => _currentState.CallSucceeds();

        internal void OnSuccess() => LastCaughtException = null;

        /// <summary>
        /// Mark a failed call through CircuitBreaker. Sometimes the callee of CircuitBreaker sends back a message to the
        /// caller Actor. In such a case, it is convenient to mark a failed call instead of using Future
        /// via <see cref="WithCircuitBreaker"/>
        /// </summary>
        public void Fail() => _currentState.CallFails(new UserCalledFailException());

        internal void OnFail(Exception cause) => LastCaughtException = cause;

        /// <summary>
        /// Return true if the internal state is Closed. WARNING: It is a "power API" call which you should use with care.
        /// Ordinal use cases of CircuitBreaker expects a remote call to return Future, as in <see cref="WithCircuitBreaker"/>.
        /// So, if you check the state by yourself, and make a remote call outside CircuitBreaker, you should
        /// manage the state yourself.
        /// </summary>
        public bool IsClosed => _currentState is Closed;

        /// <summary>
        /// Return true if the internal state is Open. WARNING: It is a "power API" call which you should use with care.
        /// Ordinal use cases of CircuitBreaker expects a remote call to return Future, as in <see cref="WithCircuitBreaker"/>.
        /// So, if you check the state by yourself, and make a remote call outside CircuitBreaker, you should
        /// manage the state yourself.
        /// </summary>
        public bool IsOpen => _currentState is Open;

        /// <summary>
        /// Return true if the internal state is HalfOpen. WARNING: It is a "power API" call which you should use with care.
        /// Ordinal use cases of CircuitBreaker expects a remote call to return Future, as in withCircuitBreaker.
        /// So, if you check the state by yourself, and make a remote call outside CircuitBreaker, you should
        /// manage the state yourself.
        /// </summary>
        public bool IsHalfOpen => _currentState is HalfOpen;

        /// <summary>
        /// Adds a callback to execute when circuit breaker opens
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnOpen(Action callback)
        {
            Open.AddListener(callback);
            return this;
        }

        /// <summary>
        /// Adds a callback to execute when circuit breaker transitions to half-open
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnHalfOpen(Action callback)
        {
            HalfOpen.AddListener(callback);
            return this;
        }

        /// <summary>
        /// Adds a callback to execute when circuit breaker state closes
        /// </summary>
        /// <param name="callback"><see cref="Action"/> Handler to be invoked on state change</param>
        /// <returns>CircuitBreaker for fluent usage</returns>
        public CircuitBreaker OnClose(Action callback)
        {
            Closed.AddListener(callback);
            return this;
        }
        
        /// <summary>
        /// The <see cref="ResetTimeout"/> will be increased exponentially for each failed attempt to close the circuit.
        /// The default exponential backoff factor is 2.
        /// </summary>
        /// <param name="maxResetTimeout">The upper bound of <see cref="ResetTimeout"/></param>
        public CircuitBreaker WithExponentialBackoff(TimeSpan maxResetTimeout)
        {
            return new CircuitBreaker(Scheduler, MaxFailures, CallTimeout, ResetTimeout, maxResetTimeout, 2.0);
        }

        /// <summary>
        /// Implements consistent transition between states. Throws IllegalStateException if an invalid transition is attempted.
        /// </summary>
        /// <param name="fromState">State being transitioning from</param>
        /// <param name="toState">State being transitioned to</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown if an invalid transition is attempted from <paramref name="fromState"/> to <paramref name="toState"/>.
        /// </exception>
        private void Transition(AtomicState fromState, AtomicState toState)
        {
            if (SwapState(fromState, toState))
            {
                Debug.WriteLine("Successful transition from {0} to {1}", fromState, toState);
                toState.Enter();
            }
            // else some other thread already swapped state
        }

        /// <summary>
        /// Trips breaker to an open state. This is valid from Closed or Half-Open states
        /// </summary>
        /// <param name="fromState">State we're coming from (Closed or Half-Open)</param>
        internal void TripBreaker(AtomicState fromState) => Transition(fromState, Open);

        /// <summary>
        /// Resets breaker to a closed state.  This is valid from an Half-Open state only.
        /// </summary>
        internal void ResetBreaker() => Transition(HalfOpen, Closed);

        /// <summary>
        /// Attempts to reset breaker by transitioning to a half-open state.  This is valid from an Open state only.
        /// </summary>
        internal void AttemptReset() => Transition(Open, HalfOpen);
    }
}
