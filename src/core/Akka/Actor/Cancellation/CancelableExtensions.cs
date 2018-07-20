//-----------------------------------------------------------------------
// <copyright file="CancelableExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2018 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2018 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public static class CancelableExtensions
    {
        /// <summary>
        /// If <paramref name="cancelable"/> is not <c>null</c> it's canceled.
        /// </summary>
        /// <param name="cancelable">The cancelable. Will be canceled if it's not <c>null</c></param>
        public static void CancelIfNotNull(this ICancelable cancelable)
        {
            if(cancelable != null) cancelable.Cancel();
        }

    }
}

