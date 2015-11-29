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
        private const string CONFIGURATION_KEY = "akka.logger.debuglogger.break-on-error";
        private readonly ILoggingAdapter _log = Context.GetLogger();
        private bool breakOnErrorMessage = false;

        private static void Log(LogEvent logEvent, bool breakOnError)
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

                if (breakOnError && logEvent.LogLevel() == LogLevel.ErrorLevel)
                {
                    Debugger.Break();
                }
            }
        }

        public DebugLogger()
        {
            Receive<Error>(m => Log(m, breakOnErrorMessage));
            Receive<Warning>(m => Log(m, breakOnErrorMessage));
            Receive<Info>(m => Log(m, breakOnErrorMessage));
            Receive<Akka.Event.Debug>(m => Log(m, breakOnErrorMessage));
            Receive<InitializeLogger>(m =>
            {
                breakOnErrorMessage = Context.System.Settings.Config.GetBoolean(CONFIGURATION_KEY, true);

                _log.Info("DebugLogger started");

                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}