//-----------------------------------------------------------------------
// <copyright file="ThrottleMode.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Streams
{
    /// <summary>
    /// Represents a mode that decides how to deal exceed rate for Throttle combinator.
    /// </summary>
    public enum ThrottleMode
    {
        /// <summary>
        /// Tells throttle to make pauses before emitting messages to meet throttle rate
        /// </summary>
        Shaping,

        /// <summary>
        /// Makes throttle fail with exception when upstream is faster than throttle rate
        /// </summary>
        Enforcing
    }
}
