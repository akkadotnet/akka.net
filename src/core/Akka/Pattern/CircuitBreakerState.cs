//-----------------------------------------------------------------------
// <copyright file="CircuitBreakerState.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Globalization;
using System.Threading.Tasks;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Pattern
{
    /// <summary>
    /// Concrete implementation of Open state
    /// </summary>
    internal class Open : AtomicState
    {
        private readonly CircuitBreaker _breaker;

        public Open( CircuitBreaker breaker )
            : base( breaker.CallTimeout, 0 )
        {
            _breaker = breaker;
        }

        /// <summary>
        /// Fail-fast on any invocation
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override Task<T> Invoke<T>( Func<Task<T>> body )
        {
            throw new OpenCircuitException( );
        }

        /// <summary>
        /// Implementation of invoke, which simply attempts the call
        /// </summary>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override Task Invoke( Func<Task> body )
        {
            throw new OpenCircuitException( );
        }

        /// <summary>
        /// No-op for open, calls are never executed so cannot succeed or fail
        /// </summary>
        protected override void CallFails( )
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// No-op for open, calls are never executed so cannot succeed or fail
        /// </summary>
        protected override void CallSucceeds( )
        {
            //throw new NotImplementedException();
        }

        /// <summary>
        /// On entering this state, schedule an attempted reset and store the entry time to
        /// calculate remaining time before attempted reset.
        /// </summary>
        protected override void EnterInternal( )
        {
            Task.Delay( _breaker.ResetTimeout ).ContinueWith( task => _breaker.AttemptReset( ) );
        }
    }

    /// <summary>
    /// Concrete implementation of half-open state
    /// </summary>
    internal class HalfOpen : AtomicState
    {
        private readonly CircuitBreaker _breaker;
        private readonly AtomicBoolean _lock;

        public HalfOpen( CircuitBreaker breaker )
            : base( breaker.CallTimeout, 0 )
        {
            _breaker = breaker;
            _lock = new AtomicBoolean();
        }

        /// <summary>
        /// Allows a single call through, during which all other callers fail-fast. If the call fails, the breaker reopens.
        /// If the call succeeds, the breaker closes.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override async Task<T> Invoke<T>( Func<Task<T>> body )
        {
            if ( !_lock.CompareAndSet( true, false) )
            {
                throw new OpenCircuitException( );
            }
            return await CallThrough( body );
        }

        /// <summary>
        /// Allows a single call through, during which all other callers fail-fast. If the call fails, the breaker reopens.
        /// If the call succeeds, the breaker closes.
        /// </summary>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override async Task Invoke( Func<Task> body )
        {
            if ( !_lock.CompareAndSet( true, false ) )
            {
                throw new OpenCircuitException( );
            }
            await CallThrough( body );
        }

        /// <summary>
        /// Reopen breaker on failed call.
        /// </summary>
        protected override void CallFails( )
        {
            _breaker.TripBreaker( this );
        }

        /// <summary>
        /// Reset breaker on successful call.
        /// </summary>
        protected override void CallSucceeds( )
        {
            _breaker.ResetBreaker( );
        }

        /// <summary>
        /// On entry, guard should be reset for that first call to get in
        /// </summary>
        protected override void EnterInternal( )
        {
            _lock.Value = true ;
        }

        /// <summary>
        /// Override for more descriptive toString
        /// </summary>
        /// <returns></returns>
        public override string ToString( )
        {
            return string.Format( CultureInfo.InvariantCulture, "Half-Open currently testing call for success = {0}", ( _lock == true ) );
        }
    }

    /// <summary>
    /// Concrete implementation of Closed state
    /// </summary>
    internal class Closed : AtomicState
    {
        private readonly CircuitBreaker _breaker;

        public Closed( CircuitBreaker breaker )
            : base( breaker.CallTimeout, 0 )
        {
            _breaker = breaker;
        }

        /// <summary>
        /// Implementation of invoke, which simply attempts the call
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override async Task<T> Invoke<T>( Func<Task<T>> body )
        {
            return await CallThrough( body );
        }

        /// <summary>
        /// Implementation of invoke, which simply attempts the call
        /// </summary>
        /// <param name="body">Implementation of the call that needs protected</param>
        /// <returns><see cref="Task"/> containing result of protected call</returns>
        public override async Task Invoke( Func<Task> body )
        {
            await CallThrough( body );
        }

        /// <summary>
        /// On failed call, the failure count is incremented.  The count is checked against the configured maxFailures, and
        /// the breaker is tripped if we have reached maxFailures.
        /// </summary>
        protected override void CallFails( )
        {
            if ( IncrementAndGet( ) == _breaker.MaxFailures )
            {
                _breaker.TripBreaker( this );
            }
        }

        /// <summary>
        /// On successful call, the failure count is reset to 0
        /// </summary>
        protected override void CallSucceeds( )
        {
            Reset();
        }

        /// <summary>
        /// On entry of this state, failure count is reset.
        /// </summary>
        protected override void EnterInternal( )
        {
            Reset();
        }

        /// <summary>
        /// Override for more descriptive toString
        /// </summary>
        /// <returns></returns>
        public override string ToString( )
        {
            return string.Format( "Closed with failure count = {0}", Current );
        }
    }
}