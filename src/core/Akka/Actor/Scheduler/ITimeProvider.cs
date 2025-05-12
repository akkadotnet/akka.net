//-----------------------------------------------------------------------
// <copyright file="ITimeProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// Time provider used by the scheduler to obtain the current time.
    /// </summary>
    /// <remarks>
    /// Intended to be customizable to we can virtualize time for testing purposes.
    ///
    /// In the future we will drop this in favor of the time provider built into .NET 8 and later.
    /// </remarks>
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

