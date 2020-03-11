//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisorBase.cs" company="Akka.NET Project">
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
    /// TBD
    /// </summary>
    public abstract class BackoffSupervisorBase : ActorBase
    {
        internal BackoffSupervisorBase(Props childProps, string childName, IBackoffReset reset, object replyWhileStopped = null, Func<object, bool> finalStopMessage = null)
        {
            ChildProps = childProps;
            ChildName = childName;
            Reset = reset;
            ReplyWhileStopped = replyWhileStopped;
            FinalStopMessage = finalStopMessage;
            Log = Logging.GetLogger(Context.System, GetType());
        }

        protected Props ChildProps { get; }
        protected string ChildName { get; }
        protected IBackoffReset Reset { get; }
        protected object ReplyWhileStopped { get; }
        protected Func<object, bool> FinalStopMessage { get; }

        protected IActorRef Child { get; set; }
        protected int RestartCountN { get; set; }
        protected bool FinalStopMessageReceived { get; set; }

        internal ILoggingAdapter Log { get; }

        protected override void PreStart()
        {
            StartChild();
            base.PreStart();
        }

        private void StartChild()
        {
            if (Child == null)
            {
                Child = Context.Watch(Context.ActorOf(ChildProps, ChildName));
            }
        }

        protected bool HandleBackoff(object message)
        {
            switch (message)
            {
                case BackoffSupervisor.StartChild _:
                    {
                        StartChild();
                        if (Reset is AutoReset backoffReset)
                        {
                            Context.System.Scheduler.ScheduleTellOnce(backoffReset.ResetBackoff, Self, new BackoffSupervisor.ResetRestartCount(RestartCountN), Self);
                        }
                        break;
                    }
                case BackoffSupervisor.Reset _ when Reset is ManualReset:
                    RestartCountN = 0;
                    break;
                case BackoffSupervisor.Reset _:
                    Unhandled(message);
                    break;
                case BackoffSupervisor.ResetRestartCount count:
                    {
                        if (count.Current == RestartCountN)
                        {
                            RestartCountN = 0;
                        }
                        break;
                    }
                case BackoffSupervisor.GetRestartCount _:
                    Sender.Tell(new BackoffSupervisor.RestartCount(RestartCountN));
                    break;
                case BackoffSupervisor.GetCurrentChild _:
                    Sender.Tell(new BackoffSupervisor.CurrentChild(Child));
                    break;
                default:
                    {
                        if (Child != null)
                        {
                            if (Child.Equals(Sender))
                            {
                                // use the BackoffSupervisor as sender
                                Context.Parent.Tell(message);
                            }
                            else
                            {
                                Child.Forward(message);
                                if (!FinalStopMessageReceived && FinalStopMessage != null)
                                {
                                    FinalStopMessageReceived = FinalStopMessage(message);
                                }
                            }
                        }
                        else
                        {
                            if (ReplyWhileStopped != null)
                            {
                                Sender.Tell(ReplyWhileStopped);
                            }
                            else
                            {
                                Context.System.DeadLetters.Forward(message);
                            }

                            if (FinalStopMessage != null && FinalStopMessage(message))
                            {
                                Context.Stop(Self);
                            }
                        }
                        break;
                    }
            }

            return true;
        }
    }
}
