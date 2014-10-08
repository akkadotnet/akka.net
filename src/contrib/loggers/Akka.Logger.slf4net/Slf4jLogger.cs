using Akka.Actor;
using Akka.Event;
using slf4net;
using System;

namespace Akka.Logger.slf4net
{
    public class Slf4NetLogger : UntypedActor
    {
        //private string mdcThreadAttributeName = "sourceThread";
        //private string mdcAkkaSourceAttributeName = "akkaSource";
        //private string mdcAkkaTimestamp = "akkaTimestamp";

        private readonly LoggingAdapter _log = Logging.GetLogger(Context);

        private void WithMDC(Action<ILogger> logStatement)
        {
            ILogger logger = LoggerFactory.GetLogger(GetType());
            logStatement(logger);
        }

        protected override void OnReceive(object message)
        {
            message
                .Match()
                .With<Error>(m => WithMDC(logger => logger.Error("{0}", m.Message)))
                .With<Warning>(m => WithMDC(logger => logger.Warn("{0}", m.Message)))
                .With<Info>(m => WithMDC(logger => logger.Info("{0}", m.Message)))
                .With<Debug>(m => WithMDC(logger => logger.Debug("{0}", m.Message)))
                .With<InitializeLogger>(m =>
                {
                    _log.Info("Slf4jLogger started");
                    Sender.Tell(new LoggerInitialized());
                });
        }
    }
}