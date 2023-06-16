//-----------------------------------------------------------------------
// <copyright file="DateTimeNowTimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public class DateTimeOffsetNowTimeProvider : ITimeProvider, IDateTimeOffsetNowTimeProvider
    {
        private static readonly DateTimeOffsetNowTimeProvider _instance = new();
        private DateTimeOffsetNowTimeProvider() { }
        /// <summary>
        /// TBD
        /// </summary>
        public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan MonotonicClock {get { return Util.MonotonicClock.Elapsed; }}

        /// <summary>
        /// TBD
        /// </summary>
        public TimeSpan HighResMonotonicClock{get { return Util.MonotonicClock.ElapsedHighRes; }}

        /// <summary>
        /// TBD
        /// </summary>
        public static DateTimeOffsetNowTimeProvider Instance { get { return _instance; } }
    }
}

