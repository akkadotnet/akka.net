using Pigeon.Dispatch.SysMsg;
using Pigeon.Event;
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
                    AutoReceiveMessage(envelope);
                }
                catch (Exception cause)
                {
                    Parent.Tell( new Failed(Self, cause));
                }
            });
        }


        /*
 def autoReceiveMessage(msg: Envelope): Unit = {
    if (system.settings.DebugAutoReceive)
      publish(Debug(self.path.toString, clazz(actor), "received AutoReceiveMessage " + msg))

    msg.message match {
      case t: Terminated              ⇒ receivedTerminated(t)
      case AddressTerminated(address) ⇒ addressTerminated(address)
      case Kill                       ⇒ throw new ActorKilledException("Kill")
      case PoisonPill                 ⇒ self.stop()
      case sel: ActorSelectionMessage ⇒ receiveSelection(sel)
      case Identify(messageId)        ⇒ sender() ! ActorIdentity(messageId, Some(self))
    }
  }
         */

        public void AutoReceiveMessage(Envelope envelope)
        {
            Pattern.Match(envelope.Message)
                .With<Terminated>(ReceivedTerminated)
                .With<Kill>(Kill)
                .With<PoisonPill>(HandlePoisonPill)
                .With<Identity>(HandleIdentity)
                .Default(m => CurrentBehavior(m));
        }

        private void ReceivedTerminated(Terminated m)
        {
            Debug.WriteLine("Terminated! actor: {0}", this.Self.Path);
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
                    /*
          case message: SystemMessage if shouldStash(message, currentState) ⇒ stash(message)
          case f: Failed ⇒ handleFailure(f)
          case DeathWatchNotification(a, ec, at) ⇒ watchedActorTerminated(a, ec, at)
          case Create(failure) ⇒ create(failure)
          case Watch(watchee, watcher) ⇒ addWatcher(watchee, watcher)
          case Unwatch(watchee, watcher) ⇒ remWatcher(watchee, watcher)
          case Recreate(cause) ⇒ faultRecreate(cause)
          case Suspend() ⇒ faultSuspend()
          case Resume(inRespToFailure) ⇒ faultResume(inRespToFailure)
          case Terminate() ⇒ terminate()
          case Supervise(child, async) ⇒ supervise(child, async)
          case NoMessage ⇒ // only here to suppress warning
                     */
                    Pattern.Match(envelope.Message)
                        .With<CompleteFuture>(HandleCompleteFuture)
                        .With<Failed>(HandleFailed)
                        .With<DeathWatchNotification>(HandleDeathWatchNotification)
                        //TODO: add create?
                        .With<Watch>(HandleWatch)
                        .With<Unwatch>(HandleUnwatch)
                        .With<Recreate>(FaultRecreate)
                        .With<Suspend>(FaultSuspend)
                        .With<Resume>(FaultResume)
                        .With<Terminate>(HandleTerminate)
                        .With<Supervise>(HandleSupervise)
                        .With<NoMessage>(m => { }) //only goes here to mimic Akka, which only goes here to supress pattern match warning :-P
                        .Default(m =>
                        {
                            throw new NotSupportedException("Unknown message " + m.GetType().Name);
                        });
                }
                catch (Exception cause)
                {
                    Parent.Tell(new Failed(Self,cause));
                }
            });
        }

        private void FaultResume(Resume obj)
        {
            
        }

        private void FaultSuspend(Suspend obj)
        {
            SuspendNonRecursive();
            SuspendChildren();
        }

        private void SuspendChildren()
        {
            foreach(var child in this.GetChildren())
            {
                child.Suspend();
            }
        }

        private void SuspendNonRecursive()
        {
            
        }

        private void HandleDeathWatchNotification(DeathWatchNotification m)
        {
            Self.Tell(new Terminated(m.Actor), m.Actor);
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

            FinishTerminate();
        }

        private void FinishTerminate()
        {

            try
            {
                TellWatchersWeDied();
            }
            catch { }
            if (Actor != null)
            {
                try
                {
                    Actor.AroundPostStop();
                }
                catch (Exception x)
                {
                    HandleNonFatalOrInterruptedException(() => Publish(new Error(x, Self.Path.ToString(), Actor.GetType(), x.Message)));
                }
            }
            Actor = null;
            Mailbox.Invoke = null;
            Mailbox.SystemInvoke = null;
            Mailbox = null;
        }

        private void HandleNonFatalOrInterruptedException(Action action)
        {
            action();
        }

        private void Publish(object message)
        {

        }

        private void TryCatch(Action action)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }

        private void TellWatchersWeDied()
        {
            WatchedBy.Tell(new DeathWatchNotification(Self,true,false));
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
            this.Self.Tell(new Recreate(null));
        }

        private void FaultRecreate(Recreate m)
        {
            isTerminating = false;
            Actor.AroundPreRestart(m.Cause, null); //TODO: pass message?
            Unbecome();//unbecome deadletters
            this.UseThreadContext(() =>
            {
                behaviorStack.Clear();
                var instance = this.Props.NewActor();
                Children.TryAdd(this.Self.Path.Name, this.Self);
                instance.AroundPostRestart(m.Cause,null);
            });
            
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

        public void Resume(Exception causedByFailure)
        {
            if (isTerminating)
                return;

            Self.Tell(new Resume(causedByFailure));
        }

		/// <summary>
		/// Async stop this actor
		/// </summary>
        public void Stop()
        {
            if (isTerminating)
                return;

            isTerminating = true;

            Self.Tell(new Terminate());            
        }

        public void Suspend()
        {
            if (isTerminating)
                return;

            Self.Tell(new Suspend());
        }

        private volatile bool isTerminating = false;
        public void Stop(LocalActorRef child)
        {
            //if (isTerminating)
            //    return;

            //isTerminating = true;
            //Debug.WriteLine("stopping child: {0}", child.Path);
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
            var handled = this.Actor.SupervisorStrategyLazy().HandleFailure(this, m.Child, m.Cause);
            if (!handled)
                throw m.Cause;
        }       

        private BroadcastActorRef WatchedBy = new BroadcastActorRef();
        private void HandleWatch(Watch m)
        {
            WatchedBy.Add(Sender);
        }

        private void HandleUnwatch(Unwatch m)
        {
            WatchedBy.Remove(Sender);
        }

        private void HandleCompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}
