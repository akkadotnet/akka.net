//-----------------------------------------------------------------------
// <copyright file="ICancelable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// Signifies something that can be canceled
    /// </summary>
    public interface ICancelable
    {
        /// <summary>
        /// Communicates a request for cancellation.
        /// </summary>
        /// <remarks>The associated cancelable will be notified of the cancellation and will transition 
        /// to a state where <see cref="IsCancellationRequested"/> returns <c>true</c>.
        /// Any callbacks or cancelable operations registered with the cancelable will be executed.
        /// Cancelable operations and callbacks registered with the token should not throw exceptions.
        /// However, this overload of Cancel will aggregate any exceptions thrown into an 
        /// <see cref="AggregateException"/>, such that one callback throwing an exception will not 
        /// prevent other registered callbacks from being executed.
        /// The <see cref="ExecutionContext"/> that was captured when each callback was registered will 
        /// be reestablished when the callback is invoked.</remarks>
        void Cancel();

        /// <summary>
        /// Gets a value indicating whether cancellation has been requested
        /// </summary>
        bool IsCancellationRequested { get; }

        /// <summary>
        /// Gets the <see cref="CancellationToken"/> associated with this <see cref="ICancelable"/>.
        /// </summary>
        CancellationToken Token { get; }

        /// <summary>
        /// Schedules a cancel operation on this cancelable after the specified delay.
        /// </summary>
        /// <param name="delay">The delay before this instance is canceled.</param>
        void CancelAfter(TimeSpan delay);

        /// <summary>
        /// Schedules a cancel operation on this cancelable after the specified number of milliseconds.
        /// </summary>
        /// <param name="millisecondsDelay">The delay in milliseconds before this instance is canceled.</param>
        void CancelAfter(int millisecondsDelay);

        /// <summary>
        /// Communicates a request for cancellation, and specifies whether remaining callbacks and 
        /// cancelable operations should be processed.
        /// </summary>
        /// <param name="throwOnFirstException"><c>true</c> if exceptions should immediately propagate; 
        /// otherwise, <c>false</c>.</param>
        /// <remarks>The associated cancelable will be notified of the cancellation and will transition 
        /// to a state where <see cref="IsCancellationRequested"/> returns <c>true</c>.
        /// Any callbacks or cancelable operations registered with the cancelable will be executed.
        /// Cancelable operations and callbacks registered with the token should not throw exceptions.
        /// If <paramref name="throwOnFirstException"/> is <c>true</c>, an exception will immediately 
        /// propagate out of the call to Cancel, preventing the remaining callbacks and cancelable operations 
        /// from being processed.
        /// If <paramref name="throwOnFirstException"/> is <c>false</c>, this overload will aggregate any exceptions 
        /// thrown into an <see cref="AggregateException"/>, such that one callback throwing an exception will not 
        /// prevent other registered callbacks from being executed.
        /// The <see cref="ExecutionContext"/> that was captured when each callback was registered will be 
        /// reestablished when the callback is invoked.</remarks>
        void Cancel(bool throwOnFirstException);
    }
}

