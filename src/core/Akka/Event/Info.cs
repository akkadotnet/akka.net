//-----------------------------------------------------------------------
// <copyright file="Info.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// This class represents an Info log event.
    /// </summary>
    public sealed class Info : LogEvent
    {
        public Info(ILogContents contents) : base(contents)
        {
        }

        public Info(string sourceName, Type sourceType, string msg) 
            : base(LogEntryExtensions.CreateLogEntryFromString(LogLevel.InfoLevel, sourceName, sourceType, msg))
        {
            
        }
    }
}
