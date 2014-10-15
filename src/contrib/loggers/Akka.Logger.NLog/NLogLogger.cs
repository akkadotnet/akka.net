using Akka.Actor;
using Akka.Event;
using NLog;
using System;
using NLogger = global::NLog.Logger;

namespace Akka.Logger.NLog
{
    public class NLogLogger : ReceiveActor
    {
        private readonly LoggingAdapter _log = Context.GetLogger();

        private static void Log(LogEvent logEvent, Action<NLogger> logStatement)
        {
            var logger = LogManager.GetLogger(logEvent.LogClass.FullName);
            logStatement(logger);
        }

        public NLogLogger()
        {
            Receive<Error>(m => Log(m, logger => logger.Error("{0}", m.Message)));
            Receive<Warning>(m => Log(m, logger => logger.Warn("{0}", m.Message)));
            Receive<Info>(m => Log(m, logger => logger.Info("{0}", m.Message)));
            Receive<Debug>(m => Log(m, logger => logger.Debug("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                _log.Info("NLogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}