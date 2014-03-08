using System;
using Akka.Actor;
using Akka.Event;
using slf4net;

namespace Akka.slf4net.Event.slf4net
{
    public class Slf4NetLogger : UntypedActor
    {
        //private string mdcThreadAttributeName = "sourceThread";
        //private string mdcAkkaSourceAttributeName = "akkaSource";
        //private string mdcAkkaTimestamp = "akkaTimestamp";

        private readonly LoggingAdapter log = Logging.GetLogger(Context);

        private void WithMDC(string logSource, LogEvent logEvent, Action<ILogger> logStatement)
        {
            ILogger logger = LoggerFactory.GetLogger(GetType());
            logStatement(logger);
        }

        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Error>(m => WithMDC(m.LogSource, m, logger => logger.Error("{0}", m.Message)))
                .With<Warning>(m => WithMDC(m.LogSource, m, logger => logger.Warn("{0}", m.Message)))
                .With<Info>(m => WithMDC(m.LogSource, m, logger => logger.Info("{0}", m.Message)))
                .With<Debug>(m => WithMDC(m.LogSource, m, logger => logger.Debug("{0}", m.Message)))
                .With<InitializeLogger>(m =>
                {
                    log.Info("Slf4jLogger started");
                    Sender.Tell(new LoggerInitialized());
                });
        }
    }
}