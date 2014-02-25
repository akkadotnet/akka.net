using Akka.Dispatch.SysMsg;
using Akka.Event;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Actor
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
            var message = envelope.Message;

            if (message is AutoReceivedMessage)
            {
                Type actorType = null;
                if (Actor != null)
                    actorType = Actor.GetType();

                if (System.Settings.DebugAutoReceive)
                    Publish(new Akka.Event.Debug(Self.Path.ToString(), actorType, "received AutoReceiveMessage " + message));

                envelope.Message
                        .Match()
                        .With<Terminated>(ReceivedTerminated)
                        .With<Kill>(Kill)
                        .With<PoisonPill>(HandlePoisonPill)
                        .With<ActorSelectionMessage>(ReceiveSelection)
                        .With<Identity>(HandleIdentity);
            }
            else
            {
                if (System.Settings.AddLoggingReceive && Actor is ILogReceive)
                {
                    //TODO: akka alters the receive handler for logging, but the effect is the same. keep it this way?
                    var UnhandledLookup = Actor.GetUnhandled();

                    CurrentBehavior(message);
                    var unhandled = UnhandledLookup(message);

                    Publish(new Akka.Event.Debug(Self.Path.ToString(), Actor.GetType(), "received " + (unhandled ? "unhandled" : "handled") + " message " + message));
                }
                else
                {
                    CurrentBehavior(message);
                }
            }           
        }

        private void ReceiveSelection(ActorSelectionMessage m)
        {
            var selection = new ActorSelection(this.Self, m.Elements.ToArray());
            selection.Tell(m.Message, Sender);
        }

        private void ReceivedTerminated(Terminated m)
        {
            var child = Child(m.actorRef.Path.Name);
            if (!child.IsNobody()) //this terminated actor is a valid child
            {
                Stop(child); //unhooks the child from the supervisor container
            }
            if (System.Settings.DebugLifecycle)
                Publish(new Akka.Event.Debug(Self.Path.ToString(), Actor.GetType(), string.Format("Terminated actor: {0}", m.actorRef.Path)));
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
                    envelope
                        .Message
                        .Match()
                        .With<CompleteFuture>(HandleCompleteFuture)
                        .With<Failed>(HandleFailed)
                        .With<DeathWatchNotification>(WatchedActorTerminated)
                        //TODO: add create?
                        //TODO: see """final def init(sendSupervise: Boolean, mailboxType: MailboxType): this.type = {""" in dispatch.scala
                        //case Create(failure) ⇒ create(failure)
                        .With<Watch>(HandleWatch)
                        .With<Unwatch>(HandleUnwatch)
                        .With<Recreate>(FaultRecreate)
                        .With<Suspend>(FaultSuspend)
                        .With<Resume>(FaultResume)
                        .With<Terminate>(Terminate)
                        .With<Supervise>(HandleSupervise)
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

        private void WatchedActorTerminated(DeathWatchNotification m)
        {
    // AKKA:
    //        if (watchingContains(actor)) {
    //  maintainAddressTerminatedSubscription(actor) {
    //    watching = removeFromSet(actor, watching)
    //  }
    //  if (!isTerminating) {
    //    self.tell(Terminated(actor)(existenceConfirmed, addressTerminated), actor)
    //    terminatedQueuedFor(actor)
    //  }
    //}
    //if (childrenRefs.getByRef(actor).isDefined) handleChildTerminated(actor)
            if (!isTerminating)
            {
                Self.Tell(new Terminated(m.Actor), m.Actor);
            }
            if (Children.ContainsKey(m.Actor.Path.Name))
            {
                HandleChildTerminated(m.Actor);
            }
        }

        private void HandleChildTerminated(ActorRef actor)
        {
            InternalActorRef tmp;
            Children.TryRemove(actor.Path.Name, out tmp);
            //global::System.Diagnostics.Debug.WriteLine("removed child " + actor.Path.Name);
            //global::System.Diagnostics.Debug.WriteLine("count " + Children.Count());
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
        private void Terminate()
        {
            if (isTerminating)
                return;

            isTerminating = true;
            
            UnwatchWatchedActors(this.Actor);
            foreach (var child in this.GetChildren())
            {
                child.Stop();
            }

            if (System.Settings.DebugLifecycle) Publish(new Akka.Event.Debug(Self.Path.ToString(), Actor.GetType(), "stopping"));
            FinishTerminate();
        }

        private void FinishTerminate()
        {
            var a = Actor;
            if (a != null)
            {
                try
                {
                    global::System.Diagnostics.Debug.WriteLine("before post stop");
                    Actor.AroundPostStop();
                }
                catch(Exception x) 
                {
                    HandleNonFatalOrInterruptedException(() => Publish(new Error(x, Self.Path.ToString(), Actor.GetType(), x.Message)));
                }
            }
            Parent.Tell(new DeathWatchNotification(Self, true, false));
            TellWatchersWeDied();
             if (System.Settings.DebugLifecycle)
                    Publish(new Akka.Event.Debug(Self.Path.ToString(), Actor.GetType(), "stopped"));
             UnwatchWatchedActors(a);

            Actor = null;
            Mailbox.Invoke = m => { };
            Mailbox.SystemInvoke = m => { };            
        }

        private void HandleNonFatalOrInterruptedException(Action action)
        {
            try
            {
                action();
            }
            catch { }
        }

        private void Publish(LogEvent @event)
        {
            try
            {
                System.EventStream.Publish(@event);
            }
            catch
            {

            }
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
            //TODO: complete this
            //  if (!isTerminating) {
            // Supervise is the first thing we get from a new child, so store away the UID for later use in handleFailure()

            //   initChild(child) match {
            //     case Some(crs) ⇒
            //       handleSupervise(child, async)
            if (System.Settings.DebugLifecycle)
            {
                Type actorType = null; //TODO: the root guardian has no actor and thus this is null.. what does akka do here?
                if (Actor != null)
                    actorType = Actor.GetType();

                Publish(new Akka.Event.Debug(Self.Path.ToString(), actorType, "now supervising " + m.Child.Path.ToString()));
            }
            //     case None ⇒ publish(Error(self.path.toString, clazz(actor), "received Supervise from unregistered child " + child + ", this will not end well"))
        }

		private void HandleIdentity(Identity m)
        {
            Sender.Tell(new ActorIdentity(m.MessageId, this.Self));
        }

        private void HandlePoisonPill()
        {
            Self.Stop();
        }

		/// <summary>
		/// Async restart this actor
		/// </summary>
        public void Restart()
        {
            this.Self.Tell(new Recreate(null));
        }

        public void Restart(Exception cause)
        {
            this.Self.Tell(new Recreate(cause));
        }

        private void FaultRecreate(Recreate m)
        {
            isTerminating = false;
            var failedActor = this.Actor;

            var optionalMessage = this.CurrentMessage;

            if (System.Settings.DebugLifecycle) 
                Publish(new Akka.Event.Debug(Self.Path.ToString(), failedActor.GetType(), "restarting"));

            try
            {
                failedActor.AroundPreRestart(m.Cause, optionalMessage);          
            }
            catch(Exception e)
            {
                HandleNonFatalOrInterruptedException(() =>
                    {
                        var ex = new PreRestartException(Self, e, m.Cause, optionalMessage);
                        Publish(new Error(ex, Self.Path.ToString(), failedActor.GetType(), e.Message));
                    });
            }
           
            this.UseThreadContext(() =>
            {
                behaviorStack.Clear(); //clear earlier behaviors

                var created = this.Props.NewActor();
                //ActorBase will register itself as the actor of this context

                if (System.Settings.DebugLifecycle)
                    Publish(new Akka.Event.Debug(Self.Path.ToString(), created.GetType(), "started (" + created + ")"));
                created.AroundPostRestart(m.Cause,null);
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
            //try
            //{
            Self.Tell(new Terminate());
            //}
            //catch
            //{
            //}
        }

        public void Suspend()
        {
            if (isTerminating)
                return;

            Self.Tell(new Suspend());
        }

        private volatile bool isTerminating = false;
        public void Stop(InternalActorRef child)
        {
            //TODO: in akka this does some wild magic on childrefs and then just call child.stop();

            //ignore this for now
            //if (Children.ContainsKey(child.Path.Name))
            //{
            //    child.Cell.Become(System.DeadLetters.Tell);
            //    LocalActorRef tmp;
            //    var name = child.Path.Name;
            //    this.Children.TryRemove(name, out tmp);
            //    Publish(new Pigeon.Event.Debug(Self.Path.ToString(), Actor.GetType(), string.Format("Child Actor {0} stopped - no longer supervising", child.Path)));
            //}

            child.Stop();
        }

        private void Kill()
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
            if (System.Settings.DebugLifecycle)
                Publish(new Akka.Event.Debug(Self.Path.ToString(), Actor.GetType(), "now watched by " + m.Watcher));
        }

        //TODO: find out why this is never called
        private void HandleUnwatch(Unwatch m)
        {            
            WatchedBy.Remove(m.Watcher);
        }

        private void HandleCompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}
