//-----------------------------------------------------------------------
// <copyright file="BackoffOnRestartSupervisor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Event;

namespace Akka.Pattern
{
    /// <summary>
    /// Back-off supervisor that stops and starts a child actor when the child actor restarts. 
    /// This back-off supervisor is created by using <see cref="BackoffSupervisor.Props(Props, string, TimeSpan, TimeSpan, double)">BackoffSupervisor.Props</see>
    /// with <see cref="Backoff.OnFailure(Props, string, TimeSpan, TimeSpan, double)">Backoff.OnFailure</see>
    /// </summary>
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
            OneForOneStrategy strategy,
            object replyWhileStopped = null,
            Func<object, bool> finalStopMessage = null) : base(childProps, childName, reset, replyWhileStopped, finalStopMessage)
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
            switch (message)
            {
                case Terminated terminated when terminated.ActorRef.Equals(childRef):
                {
                    Become(Receive);
                    Child = null;
                    var restartDelay = BackoffSupervisor.CalculateDelay(RestartCountN, _minBackoff, _maxBackoff, _randomFactor);
                    Context.System.Scheduler.ScheduleTellOnce(restartDelay, Self, BackoffSupervisor.StartChild.Instance, Self);
                    RestartCountN++;
                    break;
                }
                case BackoffSupervisor.StartChild _:
                    // Ignore it, we will schedule a new one once current child terminated.
                    break;
                default:
                    return false;
            }

            return true;
        }

        private bool OnTerminated(object message)
        {
            if (message is Terminated terminated && terminated.ActorRef.Equals(Child))
            {
                _log.Debug($"Terminating, because child {Child} terminated itself");
                Context.Stop(Self);
                return true;
            }

            return false;
        }
    }
}
