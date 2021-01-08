//-----------------------------------------------------------------------
// <copyright file="SingleDisposable (of T).cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using Nito.Disposables.Internals;

namespace Nito.Disposables
{
    /// <summary>
    /// A base class for disposables that need exactly-once semantics in a threadsafe way. All disposals of this instance block until the disposal is complete.
    /// </summary>
    /// <typeparam name="T">The type of "context" for the derived disposable. Since the context should not be modified, strongly consider making this an immutable type.</typeparam>
    /// <remarks>
    /// <para>If <see cref="Dispose()"/> is called multiple times, only the first call will execute the disposal code. Other calls to <see cref="Dispose()"/> will wait for the disposal to complete.</para>
    /// </remarks>
    public abstract class SingleDisposable<T> : IDisposable
    {
        /// <summary>
        /// The context. This is never <c>null</c>. This is empty if this instance has already been disposed (or is being disposed).
        /// </summary>
        private readonly BoundActionField<T> _context;

        private readonly ManualResetEventSlim _mre = new ManualResetEventSlim();

        /// <summary>
        /// Creates a disposable for the specified context.
        /// </summary>
        /// <param name="context">The context passed to <see cref="Dispose(T)"/>.</param>
        protected SingleDisposable(T context)
        {
            _context = new BoundActionField<T>(Dispose, context);
        }

        /// <summary>
        /// Whether this instance is currently disposing or has been disposed.
        /// </summary>
        public bool IsDisposeStarted => _context.IsEmpty;

        /// <summary>
        /// Whether this instance is disposed (finished disposing).
        /// </summary>
        public bool IsDispoed => _mre.IsSet;

        /// <summary>
        /// Whether this instance is currently disposing, but not finished yet.
        /// </summary>
        public bool IsDisposing => IsDisposeStarted && !IsDispoed;

        /// <summary>
        /// The actul disposal method, called only once from <see cref="Dispose()"/>.
        /// </summary>
        /// <param name="context">The context for the disposal operation.</param>
        protected abstract void Dispose(T context);

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        /// <remarks>
        /// <para>If <see cref="Dispose()"/> is called multiple times, only the first call will execute the disposal code. Other calls to <see cref="Dispose()"/> will wait for the disposal to complete.</para>
        /// </remarks>
        public void Dispose()
        {
            var context = _context.TryGetAndUnset();
            if (context == null)
            {
                _mre.Wait();
                return;
            }
            try
            {
                context.Invoke();
            }
            finally
            {
                _mre.Set();
            }
        }

        /// <summary>
        /// Attempts to update the stored context. This method returns <c>false</c> if this instance has already been disposed (or is being disposed).
        /// </summary>
        /// <param name="contextUpdater">The function used to update an existing context. This may be called more than once if more than one thread attempts to simultanously update the context.</param>
        protected bool TryUpdateContext(Func<T, T> contextUpdater) => _context.TryUpdateContext(contextUpdater);
    }
}
