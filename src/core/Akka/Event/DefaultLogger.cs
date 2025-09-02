//-----------------------------------------------------------------------
// <copyright file="DefaultLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// Handles incoming logger messages and events.
        /// </summary>
        /// <param name="message">The message to be processed.</param>
        /// <returns>True if the message was handled, false otherwise.</returns>
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
            {
                // Include context about the failed log event to help with debugging
                // but avoid creating a cascade of logging failures
                var logDetails = $"[{logEvent.LogLevel()}] {logEvent.LogSource}: {logEvent.Message}";
                throw new Exception($"Logger has not been initialized yet. Failed to log: {logDetails}");
            }
            
            try
            {
                _stdoutLogger.Tell(logEvent);
            }
            catch (Exception ex)
            {
                // Prevent cascade failures by not attempting to log the logging failure
                // In IIS/Windows Service environments, this prevents the feedback loop
                // that causes millions of log messages and memory exhaustion
                System.Diagnostics.Debug.WriteLine($"Failed to write to stdout logger: {ex.Message}");
            }
        }
    }
}

