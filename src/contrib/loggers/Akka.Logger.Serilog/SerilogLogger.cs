//-----------------------------------------------------------------------
// <copyright file="SerilogLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Serilog;

namespace Akka.Logger.Serilog
{
    /// <summary>
    /// This class is used to receive log events and sends them to
    /// the configured Serilog logger. The following log events are
    /// recognized: <see cref="Debug"/>, <see cref="Info"/>,
    /// <see cref="Warning"/> and <see cref="Error"/>.
    /// </summary>
    public class SerilogLogger : ReceiveActor
    {
        private readonly ILoggingAdapter _log = Context.GetLogger();

        private void WithSerilog(Action<ILogger> logStatement)
        {
            var logger = Log.Logger.ForContext("SourceContext", Context.Sender.Path);
            logStatement(logger);
        }

        private ILogger SetContextFromLogEvent(ILogger logger, LogEvent logEvent)
        {
            logger = logger
                      .ForContext("Timestamp", logEvent.Timestamp)
                      .ForContext("LogSource", logEvent.LogSource)
                      .ForContext("Thread", logEvent.Thread.ManagedThreadId.ToString().PadLeft(4, '0'));

            return logger;
        }

        private static string GetFormat(object message)
        {
            var logMessage = message as LogMessage;

            return logMessage != null
                ? logMessage.Format
                : "{Message}";
        }

        private static object[] GetArgs(object message)
        {
            var logMessage = message as LogMessage;

            return logMessage != null
                ? logMessage.Args
                : new[] { message };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SerilogLogger"/> class.
        /// </summary>
        public SerilogLogger()
        {
            Receive<Error>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Error(m.Cause, GetFormat(m.Message), GetArgs(m.Message))));
            Receive<Warning>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Warning(GetFormat(m.Message), GetArgs(m.Message))));
            Receive<Info>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Information(GetFormat(m.Message), GetArgs(m.Message))));
            Receive<Debug>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Debug(GetFormat(m.Message), GetArgs(m.Message))));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("SerilogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}

