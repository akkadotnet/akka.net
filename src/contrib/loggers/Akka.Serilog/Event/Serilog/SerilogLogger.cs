using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Serilog;

namespace Akka.Serilog.Event.Serilog
{
    public class SerilogLogger : ReceiveActor
    {
        private readonly LoggingAdapter _log = Logging.GetLogger(Context);

        private void WithSerilog(Action<ILogger> logStatement)
        {
            var logger = Log.Logger.ForContext(GetType());
            logStatement(logger);
        }

        private ILogger SetContextFromLogEvent(ILogger logger, LogEvent logEvent)
        {
            logger.ForContext("Timestamp", logEvent.Timestamp);
            logger.ForContext("LogSource", logEvent.LogSource);
            logger.ForContext("Thread", logEvent.Thread);
            return logger;
        }

        protected SerilogLogger()
        {
            Receive<Error>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Error(m.Cause, GetFormat(m), GetArgs(m))));
            Receive<Warning>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Warning(GetFormat(m), GetArgs(m))));
            Receive<Info>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Information(GetFormat(m), GetArgs(m))));
            Receive<Debug>(m => WithSerilog(logger => SetContextFromLogEvent(logger, m).Debug(GetFormat(m), GetArgs(m))));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("SerilogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }

        private static string GetFormat(object message)
        {
            var logMessage = message as LogMessage;

            return logMessage != null
                ? logMessage.Format
                : "{Message}";
        }

        private static object GetArgs(object message)
        {
            var logMessage = message as LogMessage;

            return logMessage != null
                ? logMessage.Args
                : message;
        }
    }
}
