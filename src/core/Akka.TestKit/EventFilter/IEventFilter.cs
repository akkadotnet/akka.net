﻿//-----------------------------------------------------------------------
// <copyright file="IEventFilter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Event;

namespace Akka.TestKit
{
    // ReSharper disable once InconsistentNaming
    /// <summary>
    /// TBD
    /// </summary>
    public interface IEventFilter
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logEvent">TBD</param>
        /// <returns>TBD</returns>
        bool Apply(LogEvent logEvent);
    }
}

