//-----------------------------------------------------------------------
// <copyright file="BackoffSupervisorBase.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Pattern
{
    /// <summary>
    /// TBD
    /// </summary>
    public abstract class BackoffSupervisorBase : ActorBase
    {
        internal BackoffSupervisorBase(Props childProps, string childName, IBackoffReset reset)
        {
            ChildProps = childProps;
            ChildName = childName;
            Reset = reset;
        }

        protected Props ChildProps { get; }
        protected string ChildName { get; }
        protected IBackoffReset Reset { get; }
        protected IActorRef Child { get; set; } = null;
        protected int RestartCountN { get; set; } = 0;

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
            if (message is BackoffSupervisor.StartChild)
            {
                StartChild();
                var backoffReset = Reset as AutoReset;
                if (backoffReset != null)
                {
                    Context.System.Scheduler.ScheduleTellOnce(backoffReset.ResetBackoff, Self,
                        new BackoffSupervisor.ResetRestartCount(RestartCountN), Self);
                }
            }
            else if (message is BackoffSupervisor.Reset)
            {
                if (Reset is ManualReset)
                {
                    RestartCountN = 0;
                }
                else
                {
                    Unhandled(message);
                }
            }
            else if (message is BackoffSupervisor.ResetRestartCount)
            {
                var restartCount = (BackoffSupervisor.ResetRestartCount)message;
                if (restartCount.Current == RestartCountN)
                {
                    RestartCountN = 0;
                }
            }
            else if (message is BackoffSupervisor.GetRestartCount)
            {
                Sender.Tell(new BackoffSupervisor.RestartCount(RestartCountN));
            }
            else if (message is BackoffSupervisor.GetCurrentChild)
            {
                Sender.Tell(new BackoffSupervisor.CurrentChild(Child));
            }
            else
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
                    }
                }
                else
                {
                    Context.System.DeadLetters.Forward(message);
                }
            }

            return true;
        }
    }
}
