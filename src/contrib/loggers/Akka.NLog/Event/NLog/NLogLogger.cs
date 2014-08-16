using Akka.Actor;
using Akka.Event;
using NLog;
using System;

namespace Akka.NLog.Event.NLog
{
    public class NLogLogger : ReceiveActor
    {
        private readonly LoggingAdapter log = Logging.GetLogger(Context);

        private void WithNLog(string logSource, LogEvent logEvent, Action<Logger> logStatement)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logStatement(logger);
        }

        public NLogLogger()
        {
            Receive<Error>(m => WithNLog(m.LogSource, m, logger => logger.Error("{0}", m.Message)));
            Receive<Warning>(m => WithNLog(m.LogSource, m, logger => logger.Warn("{0}", m.Message)));
            Receive<Info>(m => WithNLog(m.LogSource, m, logger => logger.Info("{0}", m.Message)));
            Receive<Debug>(m => WithNLog(m.LogSource, m, logger => logger.Debug("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                log.Info("NLogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}