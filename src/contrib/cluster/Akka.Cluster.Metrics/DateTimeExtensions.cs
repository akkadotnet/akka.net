//-----------------------------------------------------------------------
// <copyright file="DateTimeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Cluster.Metrics
{
    /// <summary>
    /// DateTimeExtensions
    /// </summary>
    internal static class DateTimeExtensions
    {
        private static readonly DateTime UnixOffset = new(1970, 1, 1);
        
        /// <summary>
        /// Converts given date and time to UNIX Timestamp - number of milliseconds elapsed since 1 Jan 1970
        /// </summary>
        public static long ToTimestamp(this DateTime dateTime)
        {
            return (long)(dateTime - UnixOffset).TotalMilliseconds;
        }
    }
}
