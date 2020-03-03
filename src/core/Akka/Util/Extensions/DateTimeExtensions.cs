//-----------------------------------------------------------------------
// <copyright file="DateTimeExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Util.Extensions
{
    /// <summary>
    /// DateTimeExtensions
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Converts given date and time to UNIX Timestamp - number of milliseconds elapsed since 1 Jan 1970
        /// </summary>
        public static long ToTimestamp(this DateTime dateTime)
        {
            return (long)(dateTime - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }
    }
}
