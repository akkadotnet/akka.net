//-----------------------------------------------------------------------
// <copyright file="SerilogLogger.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;
using Akka.Logger.Serilog.SemanticAdapter;
using Akka.Util.Internal;
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
            //Origin is the address of the sending system
            //Before Akka.Remote starts, the Origin is just set to 'local'
            var origin = "local";
            var address = Context.System.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            if (address != null)
                origin = string.Format("{0}@{1}:{2}", address.System, address.Host, address.Port);

            logger.ForContext("Origin", origin);
            logger.ForContext("Timestamp", logEvent.Timestamp);
            logger.ForContext("LogSource", logEvent.LogSource);
            logger.ForContext("Thread", logEvent.Thread);
            if (logEvent is Error)
            {
                logger.ForContext("Cause", logEvent.AsInstanceOf<Error>().Cause);
            }
            return logger;
        }

        private string GetFormat(object message)
        {
            var eventBase = message as LogEvent;
            if (eventBase != null)
            {
                var format = "{@Message}";
                var logMessage = eventBase.Message as LogMessage;
                if (logMessage != null)
                {
                    format = TemplateTransform.Transform(logMessage.Format);
                }
                return format;
            }

            return message.ToString();
        }


        private object[] GetArgs(object message)
        {
            var eventBase = message as LogEvent;
            if (eventBase != null)
            {
                var format = new[] { eventBase.Message };
                var logMessage = eventBase.Message as LogMessage;
                if (logMessage != null)
                {
                    format = logMessage.Args;
                }
                return format;
            }
            return new object[0];
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

