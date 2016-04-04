//-----------------------------------------------------------------------
// <copyright file="DateTimeNowTimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    public class DateTimeOffsetNowTimeProvider : ITimeProvider, IDateTimeOffsetNowTimeProvider
    {
        private static readonly DateTimeOffsetNowTimeProvider _instance = new DateTimeOffsetNowTimeProvider();
        private DateTimeOffsetNowTimeProvider() { }
        public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }

        public TimeSpan MonotonicClock {get { return Util.MonotonicClock.Elapsed; }}

        public TimeSpan HighResMonotonicClock{get { return Util.MonotonicClock.ElapsedHighRes; }}

        public static DateTimeOffsetNowTimeProvider Instance { get { return _instance; } }
    }
}

