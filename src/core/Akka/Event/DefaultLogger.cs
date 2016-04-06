//-----------------------------------------------------------------------
// <copyright file="DefaultLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Event
{
    /// <summary>
    /// Default logger implementation that outputs logs to the Console.
    /// </summary>
    public class DefaultLogger : ActorBase, IRequiresMessageQueue<ILoggerMessageQueueSemantics>
    {
        protected override bool Receive(object message)
        {
            if(message is InitializeLogger)
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

