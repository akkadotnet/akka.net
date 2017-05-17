//-----------------------------------------------------------------------
// <copyright file="BackoffOnRestartSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Pattern
{
    internal sealed class BackoffOnRestartSupervisor : BackoffSupervisorBase
    {
        private readonly TimeSpan _minBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly double _randomFactor;
        private readonly OneForOneStrategy _strategy;
        private readonly ILoggingAdapter _log = Context.GetLogger();

        public BackoffOnRestartSupervisor(
            Props childProps,
            string childName,
            TimeSpan minBackoff,
            TimeSpan maxBackoff,
            IBackoffReset reset,
            double randomFactor,
            OneForOneStrategy strategy) : base(childProps, childName, reset)
        {
            _minBackoff = minBackoff;
            _maxBackoff = maxBackoff;
            _randomFactor = randomFactor;
            _strategy = strategy;
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(_strategy.MaxNumberOfRetries, _strategy.WithinTimeRangeMilliseconds,
                ex =>
                {
                    var directive = _strategy.Decider?.Decide(ex) ?? Actor.SupervisorStrategy.DefaultStrategy.Decider.Decide(ex);

                    // Whatever the final Directive is, we will translate all Restarts
                    // to our own Restarts, which involves stopping the child.
                    if (directive == Directive.Restart)
                    {
                        if (_strategy.WithinTimeRangeMilliseconds > 0 && RestartCountN == 0)
                        {
                            // If the user has defined a time range for the MaxNumberOfRetries, we'll schedule a message
                            // to ourselves every time that range elapses, to reset the restart counter. We hide it
                            // behind this conditional to avoid queuing the message unnecessarily
                            Context.System.Scheduler.ScheduleTellOnce(_strategy.WithinTimeRangeMilliseconds, Self, new BackoffSupervisor.ResetRestartCount(RestartCountN), Self);
                        }
                        var childRef = Sender;
                        var nextRestartCount = RestartCountN + 1;
                        if (_strategy.MaxNumberOfRetries >= 0 && nextRestartCount > _strategy.MaxNumberOfRetries)
                        {
                            // If we've exceeded the maximum # of retries allowed by the Strategy, die.
                            _log.Debug($"Terminating on restart #{0} which exceeds max allowed restarts ({1})", nextRestartCount, _strategy.MaxNumberOfRetries);
                            Become(Receive);
                            Context.Stop(Self);
                        }
                        else
                        {
                            Become(WaitChildTerminatedBeforeBackoff(childRef));
                        }
                        
                        return Directive.Stop;
                    }
                    return directive;
                });
        }

        protected override bool Receive(object message)
        {
            return OnTerminated(message) || HandleBackoff(message);
        }

        private Receive WaitChildTerminatedBeforeBackoff(IActorRef childRef)
        {
            return message => WaitChildTerminatedBeforeBackoff(message, childRef) || HandleBackoff(message);
        }

        private bool WaitChildTerminatedBeforeBackoff(object message, IActorRef childRef)
        {
            var terminated = message as Terminated;
            if (terminated != null && terminated.ActorRef.Equals(childRef))
            {
                Become(Receive);
                Child = null;
                var restartDelay = BackoffSupervisor.CalculateDelay(RestartCountN, _minBackoff, _maxBackoff, _randomFactor);
                Context.System.Scheduler.ScheduleTellOnce(restartDelay, Self, BackoffSupervisor.StartChild.Instance, Self);
                RestartCountN++;
            }
            else if (message is BackoffSupervisor.StartChild)
            {
                // Ignore it, we will schedule a new one once current child terminated.
            }
            else
            {
                return false;
            }

            return true;
        }

        private bool OnTerminated(object message)
        {
            var terminated = message as Terminated;
            if (terminated != null && terminated.ActorRef.Equals(Child))
            {
                _log.Debug($"Terminating, because child {Child} terminated itself");
                Context.Stop(Self);
                return true;
            }

            return false;
        }
    }
}
