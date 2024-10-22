//-----------------------------------------------------------------------
// <copyright file="DateTimeNowTimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// The default <see cref="ITimeProvider"/> implementation for Akka.NET when not testing.
    /// </summary>
    [Obsolete("This class will be removed in Akka.NET v1.6.0 - use the IScheduler instead.")]
    public class DateTimeOffsetNowTimeProvider : IDateTimeOffsetNowTimeProvider
    {
        private DateTimeOffsetNowTimeProvider() { }
        
        public DateTimeOffset Now { get { return DateTimeOffset.UtcNow; } }
        
        public TimeSpan MonotonicClock {get { return Util.MonotonicClock.Elapsed; }}
        
        public TimeSpan HighResMonotonicClock{get { return Util.MonotonicClock.ElapsedHighRes; }}
        
        public static DateTimeOffsetNowTimeProvider Instance { get; } = new();
    }
}

