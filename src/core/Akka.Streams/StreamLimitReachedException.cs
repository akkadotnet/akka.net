//-----------------------------------------------------------------------
// <copyright file="StreamLimitReachedException.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public class StreamLimitReachedException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="StreamLimitReachedException"/> class.
        /// </summary>
        /// <param name="max">The maximum number of streams</param>
        public StreamLimitReachedException(long max) : base($"Limit of {max} reached")
        {
        }
    }
}
