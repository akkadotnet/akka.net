//-----------------------------------------------------------------------
// <copyright file="AlreadyCanceledCancelable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// A <see cref="ICancelable"/> that is already canceled.
    /// </summary>
    internal sealed class AlreadyCanceledCancelable : ICancelable
    {
        private AlreadyCanceledCancelable() { }

        /// <inheritdoc/>
        public void Cancel()
        {
            //Intentionally left blank
        }

        /// <inheritdoc/>
        public bool IsCancellationRequested => true;

        /// <summary>
        /// Gets an instance of an already canceled <see cref="ICancelable"/>.
        /// </summary>
        public static ICancelable Instance { get; } = new AlreadyCanceledCancelable();

        /// <inheritdoc/>
        public CancellationToken Token => new(true);

        void ICancelable.CancelAfter(TimeSpan delay)
        {
            //Intentionally left blank            
        }

        void ICancelable.CancelAfter(int millisecondsDelay)
        {
            //Intentionally left blank            
        }

        void ICancelable.Cancel(bool throwOnFirstException)
        {
            //Intentionally left blank
        }
    }
}

