//-----------------------------------------------------------------------
// <copyright file="DefaultLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        private MinimalLogger _stdoutLogger;
        
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case InitializeLogger _:
                    _stdoutLogger = Context.System.Settings.StdoutLogger;
                    Sender.Tell(new LoggerInitialized());
                    return true;
                case LogEvent logEvent:
                    Print(logEvent);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Print the specified log event.
        /// </summary>
        /// <param name="logEvent">The log event that is to be output.</param>
        protected virtual void Print(LogEvent logEvent)
        {
            if (_stdoutLogger == null)
                throw new Exception("Logger has not been initialized yet.");
            
            _stdoutLogger.Tell(logEvent);
        }
    }
}

