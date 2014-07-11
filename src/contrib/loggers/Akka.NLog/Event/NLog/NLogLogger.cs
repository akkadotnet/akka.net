using Akka.Actor;
using Akka.Event;
using NLog;
using System;

namespace Akka.NLog.Event.NLog
{
    public class NLogLogger : UntypedActor
    {
        private readonly LoggingAdapter log = Logging.GetLogger(Context);

        private void WithNLog(string logSource, LogEvent logEvent, Action<Logger> logStatement)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logStatement(logger);
        }

        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Error>(m => WithNLog(m.LogSource, m, logger => logger.Error("{0}", m.Message)))
                .With<Warning>(m => WithNLog(m.LogSource, m, logger => logger.Warn("{0}", m.Message)))
                .With<Info>(m => WithNLog(m.LogSource, m, logger => logger.Info("{0}", m.Message)))
                .With<Debug>(m => WithNLog(m.LogSource, m, logger => logger.Debug("{0}", m.Message)))
                .With<InitializeLogger>(m =>
                {
                    log.Info("NLogLogger started");
                    Sender.Tell(new LoggerInitialized());
                });
        }
    }
}