//-----------------------------------------------------------------------
// <copyright file="Debug.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a Debug log event.
    /// </summary>
    public sealed class Debug : LogEvent
    {
        public Debug(ILogContents contents) : base(contents)
        {
        }

        public Debug(string sourceName, Type actorType, string msg) 
            : base(LogEntryExtensions.CreateLogEntryFromString(LogLevel.DebugLevel, sourceName, actorType, msg))
        {
        }
    }
}
