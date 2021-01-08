//-----------------------------------------------------------------------
// <copyright file="ITimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>
        /// Gets the scheduler's notion of current time.
        /// </summary>
        DateTimeOffset Now { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TimeSpan MonotonicClock { get; }
        /// <summary>
        /// TBD
        /// </summary>
        TimeSpan HighResMonotonicClock { get; }
    }
}

