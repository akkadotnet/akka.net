//-----------------------------------------------------------------------
// <copyright file="DebugLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Represents a logger that logs using System.Diagnostics.Debug and breaks attached debuggers on errors.
    /// </summary>
    public class DebugLogger : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private static void Log(LogEvent logEvent)
        {
            System.Diagnostics.Debug.WriteLine(
                logEvent.ToString(),
                logEvent.LogLevel().ToString());

            if (Debugger.IsAttached)
            {
                if (Debugger.IsLogging())
                {
                    Debugger.Log(
                        (int)logEvent.LogLevel(),
                        logEvent.LogLevel().ToString(),
                        logEvent.ToString());
                }

                if (logEvent.LogLevel() == LogLevel.ErrorLevel)
                {
                    Debugger.Break();
                }
            }
        }

        public DebugLogger()
        {
            Receive<Error>(m => Log(m));
            Receive<Warning>(m => Log(m));
            Receive<Info>(m => Log(m));
            Receive<Akka.Event.Debug>(m => Log(m));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("DebugLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}