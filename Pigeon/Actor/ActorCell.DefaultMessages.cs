using Pigeon.Dispatch.SysMsg;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public partial class ActorCell 
    {
        public void Invoke(Envelope envelope)
        {
            this.CurrentMessage = envelope.Message;
            this.Sender = envelope.Sender;
            //set the current context
            UseThreadContext(() =>
            {
                try
                {
                    Default(envelope.Message);
                }
                catch (Exception cause)
                {
                    Parent.Tell( new Failed(Self, cause));
                }
            });
        }

        public void SystemInvoke(Envelope envelope)
        {
            this.CurrentMessage = envelope.Message;
            this.Sender = envelope.Sender;
            //set the current context
            UseThreadContext(() =>
            {
                try
                {
                    Pattern.Match(envelope.Message)
                        .With<DeathWatchNotification>(HandleDeathWatchNotification)
                        .With<Terminate>(HandleTerminate)
                        .With<Supervise>(HandleSupervise)
                        //kill this actor
                        .With<Kill>(Kill)
                        //request to stop a child
                        .With<StopChild>(m => StopChild(m.Child))
                        //request to restart a child
                        .With<ReCreate>(FaultReCreate)
                        //kill this actor
                        .With<PoisonPill>(HandlePoisonPill)
                        //someone is watching us
                        .With<Watch>(HandleWatch)
                        //someone is unwatching us
                        .With<Unwatch>(HandleUnwatch)
                        //complete the future callback by setting the result in this thread
                        .With<CompleteFuture>(HandleCompleteFuture)
                        //supervice exception from child actor
                        .With<Failed>(HandleFailed)
                        //handle any other message
                        .With<Identity>(HandleIdentity)
                        .Default(m => { throw new NotImplementedException(); });
                }
                catch (Exception cause)
                {
                    Parent.Tell(new Failed(Self,cause));
                }
            });
        }

        private void HandleDeathWatchNotification(DeathWatchNotification m)
        {
            
        }


        /*
protected def terminate() {
  
    // prevent Deadletter(Terminated) messages
    unwatchWatchedActors(actor)

    // stop all children, which will turn childrenRefs into TerminatingChildrenContainer (if there are children)
    children foreach stop

    val wasTerminating = isTerminating

    if (setChildrenTerminationReason(ChildrenContainer.Termination)) {
      if (!wasTerminating) {
        // do not process normal messages while waiting for all children to terminate
        suspendNonRecursive()
        // do not propagate failures during shutdown to the supervisor
        setFailed(self)
        if (system.settings.DebugLifecycle) publish(Debug(self.path.toString, clazz(actor), "stopping"))
      }
    } else {
      setTerminated()
      finishTerminate()
    }
  }
         */
        private void HandleTerminate(Terminate m)
        {
            isTerminating = true;
            UnwatchWatchedActors(this.Actor);
            foreach (var child in this.GetChildren())
            {
                child.Stop();
            }
        }

        private void UnwatchWatchedActors(ActorBase actorBase)
        {
            foreach(var watchee in Watchees)
            {
                watchee.Tell(new Unwatch(watchee, Self));
            }
        }
        private void HandleSupervise(Supervise m)
        {
        }

		private void HandleIdentity(Identity m)
        {
            Sender.Tell(new ActorIdentity(m.MessageId, this.Self));
        }

        private void HandlePoisonPill(PoisonPill m)
        {

        }

		/// <summary>
		/// Async restart this actor
		/// </summary>
        public void Restart()
        {
            this.Self.Tell(new ReCreate(null));
        }

        private void FaultReCreate(ReCreate m)
        {
            isTerminating = false;
            Unbecome();//unbecome deadletters
            NewActor(this);
        }

        public void Start()
        {
            if (isTerminating)
                return;

            if (Parent != null)
            {
                Parent.Tell(new Supervise(Self, false));
            }
        }

		/// <summary>
		/// Async stop this actor
		/// </summary>
        public void Stop()
        {
            if (isTerminating)
                return;

            Self.Tell(new Terminate());            
        }

        private volatile bool isTerminating = false;
        private void StopChild(LocalActorRef child)
        {
            if (isTerminating)
                return;

            isTerminating = true;
            Debug.WriteLine("stopping child: {0}", child.Path);
            child.Cell.Become(System.DeadLetters.Tell);
            LocalActorRef tmp;
            var name = child.Path.Name;
            this.Children.TryRemove(name, out tmp);
        }

        private void Kill(Kill m)
        {
            throw new ActorKilledException("Kill");
        }
        private void Default(object m)
        {
            CurrentBehavior(m);
        }

        private void HandleFailed(Failed m)
        {
            switch (this.Actor.SupervisorStrategyLazy().Handle(Sender, m.Cause))
            {
                case Directive.Escalate:
                    throw m.Cause;
                case Directive.Resume:
                    break;
                case Directive.Restart:
                    m.Child.Tell(new ReCreate(m.Cause));
                    break;
                case Directive.Stop:
                    StopChild((LocalActorRef)Sender);
                    break;
                default:
                    break;
            }
        }        

        private BroadcastActorRef Watchers = new BroadcastActorRef();
        private void HandleWatch(Watch m)
        {
            Watchers.Add(Sender);
        }

        private void HandleUnwatch(Unwatch m)
        {
            Watchers.Remove(Sender);
        }

        private void HandleCompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}
