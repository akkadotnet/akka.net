using Akka.Actor;
using Akka.Event;
using NLog;
using System;

namespace Akka.NLog
{
    public class NLogLogger : ReceiveActor
    {
        private readonly LoggingAdapter _log = Logging.GetLogger(Context);

        private static void WithNLog(Action<Logger> logStatement)
        {
            var logger = LogManager.GetCurrentClassLogger();
            logStatement(logger);
        }

        public NLogLogger()
        {
            Receive<Error>(m => WithNLog(logger => logger.Error("{0}", m.Message)));
            Receive<Warning>(m => WithNLog(logger => logger.Warn("{0}", m.Message)));
            Receive<Info>(m => WithNLog(logger => logger.Info("{0}", m.Message)));
            Receive<Debug>(m => WithNLog(logger => logger.Debug("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("NLogLogger started");
                Sender.Tell<LoggerInitialized>();
            });
        }
    }
}