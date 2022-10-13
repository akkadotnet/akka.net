//-----------------------------------------------------------------------
// <copyright file="Warning.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a Warning log event.
    /// </summary>
    public sealed class Warning : LogEvent
    {
        public Warning(ILogContents contents) : base(contents)
        {
        }
        
        public Warning(string sourceName, Type sourceType, string msg) 
            : base(LogEntryExtensions.CreateLogEntryFromString(LogLevel.WarningLevel, sourceName, sourceType, msg))
        {
            throw new NotImplementedException();
        }

        public Warning(Exception exception, string sourceName, Type sourceType, string msg) 
            : base(LogEntryExtensions.CreateLogEntryFromString(LogLevel.WarningLevel, sourceName, sourceType, msg, exception))
        {
            throw new NotImplementedException();
        }
    }
}
