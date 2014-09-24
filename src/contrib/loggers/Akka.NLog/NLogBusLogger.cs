using Akka.Actor;
using Akka.Event;
using NLog;

namespace Akka.NLog
{
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

        //protected override void OnReceive(object message)
        //{
        //    message.Match()
        //    .With<InitializeLogger>(m =>
        //    {
        //        var bus = m.LoggingBus;

        //        bus.Subscribe(Self, typeof(LogEvent));
        //        bus.Subscribe(Self, typeof(SetTarget));
        //        bus.Subscribe(Self, typeof(UnhandledMessage));

        //        Sender.Tell(new LoggerInitialized());
        //    })
        //        //.With<SetTarget>(m =>
        //        //{
        //        //    _dst = m.Ref;
        //        //    _dst.Tell("OK");
        //        //})
        //    .With<LogEvent>(m => _logger.Log(m.ToLogEventInfo()))
        //    .With<UnhandledMessage>(m => _dst.Tell(m));
        //}
    }
}