using Akka.Actor;
using Akka.Event;
using Monitor.Actors.Events;
using Monitor.Exceptions;
using Monitor.Extensions;
using System.Collections.Generic;

namespace Monitor.Actors
{
    public class MonitorSupervisor : ReceiveActor
    {
        private readonly LoggingAdapter _logger = Logging.GetLogger(Context);
        private readonly Dictionary<string, ActorRef> _children = new Dictionary<string, ActorRef>();

        public MonitorSupervisor()
        {
            Receive<SpawnMonitor>(OnSpawnMonitor);
            Context.ActorOf(Props.Create(() => new MonitorResultProcessor()), "processor");
        }

        private bool OnSpawnMonitor(SpawnMonitor @event)
        {
            if (!_children.ContainsKey(@event.Url))
            {
                var actor = Context.ActorOf(Props.Create(() => new global::Monitor.Actors.Monitor(@event.Url, @event.TimeOut)), @event.Name);
                Context.System.Scheduler.Schedule(0.Seconds(), @event.Interval, actor, new RunMonitoringCheck());
                return true;
            }

            _logger.Warn("The requested website is already monitored. Message wasn't handled");
            return false;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: 10.Seconds(),
                decider: x =>
                {
                    if (x.InnerException is NonFatalErrorException)
                    {
                        _logger.Warn("Non fatal exception occured : {0}, Resuming the actor", x.ToMessageAndCompleteStacktrace());
                        return Directive.Resume;
                    }

                    if (x.InnerException is FatalErrorException)
                    {
                        _logger.Error("Fatal exception occured : {0}, Restarting the actor", x.ToMessageAndCompleteStacktrace());
                        return Directive.Restart;
                    }

                    _logger.Warn("An unhandled exception occured : {0}, Stopping the actor", x.ToMessageAndCompleteStacktrace());
                    return Directive.Stop;
                });
        }
    }
}