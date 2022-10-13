//-----------------------------------------------------------------------
// <copyright file="Error.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Dispatch;

namespace Akka.Event
{
    /// <summary>
    /// This class represents a Error log event.
    /// </summary>
    public sealed class Error : LogEvent
    {
        public Error(ILogContents contents) : base(contents)
        {
        }

        public Error(Exception cause, string fullName, Type getType, string msg) 
            : base(LogEntryExtensions.CreateLogEntryFromString(LogLevel.ErrorLevel, fullName, getType, msg, cause))
        {
        }
    }
}
