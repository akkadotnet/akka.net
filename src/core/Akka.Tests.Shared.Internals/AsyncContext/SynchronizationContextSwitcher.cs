//-----------------------------------------------------------------------
// <copyright file="SynchronizationContextSwitcher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nito.AsyncEx
{
    /// <summary>
    /// Utility class for temporarily switching <see cref="SynchronizationContext"/> implementations.
    /// </summary>
    public sealed class SynchronizationContextSwitcher : Disposables.SingleDisposable<object>
    {
        /// <summary>
        /// The previous <see cref="SynchronizationContext"/>.
        /// </summary>
        private readonly SynchronizationContext _oldContext;

        /// <summary>
        /// Initializes a new instance of the <see cref="SynchronizationContextSwitcher"/> class, installing the new <see cref="SynchronizationContext"/>.
        /// </summary>
        /// <param name="newContext">The new <see cref="SynchronizationContext"/>. This can be <c>null</c> to remove an existing <see cref="SynchronizationContext"/>.</param>
        public SynchronizationContextSwitcher(SynchronizationContext newContext)
            : base(new object())
        {
            _oldContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(newContext);
        }

        /// <summary>
        /// Restores the old <see cref="SynchronizationContext"/>.
        /// </summary>
        protected override void Dispose(object context)
        {
            SynchronizationContext.SetSynchronizationContext(_oldContext);
        }

        /// <summary>
        /// Executes a synchronous delegate without the current <see cref="SynchronizationContext"/>. The current context is restored when this function returns.
        /// </summary>
        /// <param name="action">The delegate to execute.</param>
        public static void NoContext(Action action)
        {
            using (new SynchronizationContextSwitcher(null))
                action();
        }

        /// <summary>
        /// Executes an asynchronous delegate without the current <see cref="SynchronizationContext"/>. The current context is restored when this function returns its task.
        /// </summary>
        /// <param name="action">The delegate to execute.</param>
        public static Task NoContextAsync(Func<Task> action)
        {
            using (new SynchronizationContextSwitcher(null))
                return action();
        }
    }
}
