//-----------------------------------------------------------------------
// <copyright file="AlreadyCanceledCancelable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading;

namespace Akka.Actor
{
    /// <summary>
    /// A <see cref="ICancelable"/> that is already canceled.
    /// </summary>
    public class AlreadyCanceledCancelable : ICancelable
    {
        private static readonly AlreadyCanceledCancelable _instance = new AlreadyCanceledCancelable();

        private AlreadyCanceledCancelable() { }

        /// <summary>
        /// TBD
        /// </summary>
        public void Cancel()
        {
            //Intentionally left blank
        }

        /// <summary>
        /// TBD
        /// </summary>
        public bool IsCancellationRequested { get { return true; } }

        /// <summary>
        /// TBD
        /// </summary>
        public static ICancelable Instance { get { return _instance; } }

        /// <summary>
        /// TBD
        /// </summary>
        public CancellationToken Token
        {
            get { return new CancellationToken(true); }
        }

        void ICancelable.CancelAfter(TimeSpan delay)
        {
            //Intentionally left blank            
        }

        void ICancelable.CancelAfter(int millisecondsDelay)
        {
            //Intentionally left blank            
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="throwOnFirstException">TBD</param>
        public void Cancel(bool throwOnFirstException)
        {
            //Intentionally left blank
        }
    }
}

