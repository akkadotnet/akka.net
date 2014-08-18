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
    public class SerilogLogger:ReceiveActor
    {
        private readonly LoggingAdapter log = Logging.GetLogger(Context);

        private void WithSerilog(Action<ILogger> logStatement)
        {
            var logger = Log.Logger.ForContext(GetType());
            logStatement(logger);
        }
        protected SerilogLogger()
        {
            Receive<Error>(m => WithSerilog(logger => logger.Error("{0}", m.Message)));
            Receive<Warning>(m => WithSerilog(logger => logger.Warning("{0}", m.Message)));
            Receive<Info>(m => WithSerilog(logger => logger.Information("{0}", m.Message)));
            Receive<Debug>(m => WithSerilog(logger => logger.Debug("{0}", m.Message)));
            Receive<InitializeLogger>(m =>
            {
                log.Info("SerilogLogger started");
                Sender.Tell(new LoggerInitialized());
            });
        }
    }
}
