//-----------------------------------------------------------------------
// <copyright file="AlreadyCanceledCancelable.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public void Cancel()
        {
            //Intentionally left blank
        }

        public bool IsCancellationRequested { get { return true; } }

        public static ICancelable Instance { get { return _instance; } }

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

        public void Cancel(bool throwOnFirstException)
        {
            //Intentionally left blank
        }
    }
}
