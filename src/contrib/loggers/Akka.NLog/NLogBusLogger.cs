using Akka.Actor;
using Akka.Event;
using NLog;

namespace Akka.NLog
{
    /// <summary>
    /// The NLogBusLogger consumes the Akka.Event.LogEvent(s) from the logging bus.
    /// </summary>
    public class NLogBusLogger : ReceiveActor
    {
        private readonly Logger _logger;
        private readonly ActorRef _dst = Context.System.DeadLetters;

        public NLogBusLogger()
        {
            _logger = LogManager.GetCurrentClassLogger();

            Receive<InitializeLogger>(msg =>
            {
                var bus = msg.LoggingBus;

                bus.Subscribe(Self, typeof(LogEvent));
                bus.Subscribe(Self, typeof(UnhandledMessage));

                Sender.Tell<LoggerInitialized>();
            });

            Receive<LogEvent>(msg => _logger.Log(msg.ToLogEventInfo()));
            Receive<UnhandledMessage>(msg => _dst.Tell(msg));
        }
    }
}