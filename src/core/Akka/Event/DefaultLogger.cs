//-----------------------------------------------------------------------
// <copyright file="DefaultLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    ///     Class DefaultLogger.
    /// </summary>
    public class DefaultLogger : ActorBase, ISyncActor
    {
        /// <summary>
        ///     Processor for user defined messages.
        /// </summary>
        /// <param name="message">The message.</param>
        protected override bool Receive(object message)
        {
            if(message is InitializeLogger)
            {
                Sender.Tell(new LoggerInitialized());
                return true;
            }
            var logEvent = message as LogEvent;
            if(logEvent != null)
            {
                Print(logEvent);
                return true;
            }
            return false;            
        }

        protected virtual void Print(LogEvent logEvent)
        {
            StandardOutLogger.PrintLogEvent(logEvent);
        }
    }
}

