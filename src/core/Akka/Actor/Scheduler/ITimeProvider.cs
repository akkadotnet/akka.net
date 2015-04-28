//-----------------------------------------------------------------------
// <copyright file="ITimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    public interface ITimeProvider
    {
        /// <summary>
        /// Gets the scheduler's notion of current time.
        /// </summary>
        DateTimeOffset Now { get; }
        TimeSpan MonotonicClock { get; }
        TimeSpan HighResMonotonicClock { get; }
    }
}

