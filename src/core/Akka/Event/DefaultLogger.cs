//-----------------------------------------------------------------------
// <copyright file="DefaultLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Event
{
    /// <summary>
    /// Default logger implementation that outputs logs to the Console.
    /// </summary>
    public class DefaultLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            if (message is InitializeLogger)
            {
                Sender.Tell(new LoggerInitialized());
                return true;
            }
            var logEvent = message as LogEvent;
            if (logEvent == null)
                return false;

            Print(logEvent);
            return true;
        }

        /// <summary>
        /// Print the specified log event.
        /// </summary>
        /// <param name="logEvent">The log event that is to be output.</param>
        protected virtual void Print(LogEvent logEvent)
        {
            StandardOutLogger.PrintLogEvent(logEvent);
        }
    }
}

