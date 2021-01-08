//-----------------------------------------------------------------------
// <copyright file="AsyncContext.SynchronizationContext.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Nito.AsyncEx.Synchronous;

namespace Nito.AsyncEx
{
    public sealed partial class AsyncContext
    {
        /// <summary>
        /// The <see cref="SynchronizationContext"/> implementation used by <see cref="AsyncContext"/>.
        /// </summary>
        private sealed class AsyncContextSynchronizationContext : SynchronizationContext
        {
            /// <summary>
            /// The async context.
            /// </summary>
            private readonly AsyncContext _context;

            /// <summary>
            /// Initializes a new instance of the <see cref="AsyncContextSynchronizationContext"/> class.
            /// </summary>
            /// <param name="context">The async context.</param>
            public AsyncContextSynchronizationContext(AsyncContext context)
            {
                _context = context;
            }

            /// <summary>
            /// Gets the async context.
            /// </summary>
            public AsyncContext Context
            {
                get
                {
                    return _context;
                }
            }

            /// <summary>
            /// Dispatches an asynchronous message to the async context. If all tasks have been completed and the outstanding asynchronous operation count is zero, then this method has undefined behavior.
            /// </summary>
            /// <param name="d">The <see cref="T:System.Threading.SendOrPostCallback"/> delegate to call. May not be <c>null</c>.</param>
            /// <param name="state">The object passed to the delegate.</param>
            public override void Post(SendOrPostCallback d, object state)
            {
                _context.Enqueue(_context._taskFactory.StartNew(() => d(state)), true);
            }

            /// <summary>
            /// Dispatches an asynchronous message to the async context, and waits for it to complete.
            /// </summary>
            /// <param name="d">The <see cref="T:System.Threading.SendOrPostCallback"/> delegate to call. May not be <c>null</c>.</param>
            /// <param name="state">The object passed to the delegate.</param>
            public override void Send(SendOrPostCallback d, object state)
            {
                if (AsyncContext.Current == _context)
                {
                    d(state);
                }
                else
                {
                    var task = _context._taskFactory.StartNew(() => d(state));
                    task.WaitAndUnwrapException();
                }
            }

            /// <summary>
            /// Responds to the notification that an operation has started by incrementing the outstanding asynchronous operation count.
            /// </summary>
            public override void OperationStarted()
            {
                _context.OperationStarted();
            }

            /// <summary>
            /// Responds to the notification that an operation has completed by decrementing the outstanding asynchronous operation count.
            /// </summary>
            public override void OperationCompleted()
            {
                _context.OperationCompleted();
            }

            /// <summary>
            /// Creates a copy of the synchronization context.
            /// </summary>
            /// <returns>A new <see cref="T:System.Threading.SynchronizationContext"/> object.</returns>
            public override SynchronizationContext CreateCopy()
            {
                return new AsyncContextSynchronizationContext(_context);
            }

            /// <summary>
            /// Returns a hash code for this instance.
            /// </summary>
            /// <returns>A hash code for this instance, suitable for use in hashing algorithms and data structures like a hash table.</returns>
            public override int GetHashCode()
            {
                return _context.GetHashCode();
            }

            /// <summary>
            /// Determines whether the specified <see cref="System.Object"/> is equal to this instance. It is considered equal if it refers to the same underlying async context as this instance.
            /// </summary>
            /// <param name="obj">The <see cref="System.Object"/> to compare with this instance.</param>
            /// <returns><c>true</c> if the specified <see cref="System.Object"/> is equal to this instance; otherwise, <c>false</c>.</returns>
            public override bool Equals(object obj)
            {
                var other = obj as AsyncContextSynchronizationContext;
                if (other == null)
                    return false;
                return (_context == other._context);
            }
        }
    }
}
