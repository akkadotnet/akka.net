using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util;
using Debug = Akka.Event.Debug;

namespace Akka.Actor
{
    /// <summary>
    ///     Class ActorCell.
    /// </summary>
    [DebuggerDisplay("{Self,nq}")]
    public partial class ActorCell
    {
        //terminatedqueue should never be used outside the message loop
        private readonly HashSet<ActorRef> terminatedQueue = new HashSet<ActorRef>();
        /// <summary>
        ///     The watched by
        /// </summary>
        private readonly BroadcastActorRef WatchedBy = new BroadcastActorRef();

        /// <summary>
        ///     The is terminating
        /// </summary>
        private volatile bool isTerminating;

        /// <summary>
        ///     Gets the type of the actor.
        /// </summary>
        /// <value>The type of the actor.</value>
        private Type ActorType
        {
            get
            {
                if (_actor != null)
                    return _actor.GetType();
                return GetType();
            }
        }

        /// <summary>
        ///     Stops the specified child.
        /// </summary>
        /// <param name="child">The child.</param>
        public void Stop(ActorRef child)
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

            ((InternalActorRef)child).Stop();
        }

        /// <summary>
        ///     Invokes the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        public void Invoke(Envelope envelope)
        {
            var message = envelope.Message;
            CurrentMessage = message;
            Sender = envelope.Sender;

            try
            {
                var autoReceivedMessage = message as AutoReceivedMessage;
                if(autoReceivedMessage!=null)
                    AutoReceiveMessage(envelope);
                else
                    ReceiveMessage(message);
            }
            catch(Exception cause)
            {
                Mailbox.Suspend();
                Parent.Tell(new Failed(Self, cause));
            }

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

        protected virtual void AutoReceiveMessage(Envelope envelope)
        {
            var message = envelope.Message;

            var actor = _actor;
            var actorType = actor != null ? actor.GetType() : null;

            if(System.Settings.DebugAutoReceive)
                Publish(new Debug(Self.Path.ToString(), actorType, "received AutoReceiveMessage " + message));

            envelope.Message
                .Match()
                .With<Terminated>(ReceivedTerminated)
                .With<Kill>(Kill)
                .With<PoisonPill>(HandlePoisonPill)
                .With<ActorSelectionMessage>(ReceiveSelection)
                .With<Identify>(HandleIdentity);
        }

        /// <summary>
        /// This is only intended to be called from TestKit's TestActorRef
        /// </summary>
        /// <param name="envelope"></param>
        public void ReceiveMessageForTest(Envelope envelope)
        {
            var message = envelope.Message;
            CurrentMessage = message;
            Sender = envelope.Sender;
            try
            {
                ReceiveMessage(message);
            }
            finally
            {
                CurrentMessage = null;
                Sender = System.DeadLetters;
            }
        }

        internal void ReceiveMessage(object message)
        {
            var wasHandled = _actor.AroundReceive(behaviorStack.Peek(), message);

            if(System.Settings.AddLoggingReceive && _actor is ILogReceive)
            {
                //TODO: akka alters the receive handler for logging, but the effect is the same. keep it this way?
                Publish(new Debug(Self.Path.ToString(), _actor.GetType(),
                    "received " + (wasHandled ? "handled" : "unhandled") + " message " + message));
            }
        }

        /// <summary>
        ///     Receives the selection.
        /// </summary>
        /// <param name="m">The m.</param>
        private void ReceiveSelection(ActorSelectionMessage m)
        {
            var selection = new ActorSelection(Self, m.Elements.ToArray());
            selection.Tell(m.Message, Sender);
        }

        /// <summary>
        ///     Receiveds the terminated.
        /// </summary>
        /// <param name="m">The m.</param>
        private void ReceivedTerminated(Terminated m)
        {
            if (terminatedQueue.Contains(m.ActorRef))
            {
                terminatedQueue.Remove(m.ActorRef);
                this.ReceiveMessage(m);
            }

            ////TODO: we can get here from actors that we just watch, we should not try to stop things if we are not the parent of the actor(?)
            //InternalActorRef child = Child(m.ActorRef.Path.Name);
            //if (!child.IsNobody()) //this terminated actor is a valid child
            //{
            //    Stop(child); //unhooks the child from the supervisor container
            //}
            //if (System.Settings.DebugLifecycle)
            //    Publish(new Debug(Self.Path.ToString(), Actor.GetType(),
            //        string.Format("Terminated actor: {0}", m.ActorRef.Path)));

            //
            
        }

        /// <summary>
        ///     Systems the invoke.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        public void SystemInvoke(Envelope envelope)
        {
            CurrentMessage = envelope.Message;
            Sender = envelope.Sender;
            //set the current context

                try
                {
                    envelope
                        .Message
                        .Match()
                        .With<CompleteFuture>(HandleCompleteFuture)
                        .With<Failed>(HandleFailed)
                        .With<DeathWatchNotification>(WatchedActorTerminated)
                        .With<Create>(HandleCreate)
                        //TODO: see """final def init(sendSupervise: Boolean, mailboxType: MailboxType): this.type = {""" in dispatch.scala
                        //case Create(failure) ⇒ create(failure)
                        .With<Watch>(HandleWatch)
                        .With<Unwatch>(HandleUnwatch)
                        .With<Recreate>(FaultRecreate)
                        .With<Suspend>(FaultSuspend)
                        .With<Resume>(FaultResume)
                        .With<Terminate>(Terminate)
                        .With<Supervise>(s => Supervise(s.Child, s.Async))
                        .Default(m => { throw new NotSupportedException("Unknown message " + m.GetType().Name); });
                }
                catch (Exception cause)
                {
                    Parent.Tell(new Failed(Self, cause));
                }

        }

        /// <summary>
        ///     Faults the resume.
        /// </summary>
        /// <param name="obj">The object.</param>
        private void FaultResume(Resume obj)
        {
            Mailbox.Resume();
        }

        /// <summary>
        ///     Faults the suspend.
        /// </summary>
        /// <param name="obj">The object.</param>
        private void FaultSuspend(Suspend obj)
        {
            SuspendNonRecursive();
            SuspendChildren();
        }

        /// <summary>
        ///     Suspends the children.
        /// </summary>
        private void SuspendChildren()
        {
            foreach (InternalActorRef child in GetChildren())
            {
                child.Suspend();
            }
        }

        /// <summary>
        ///     Suspends the non recursive.
        /// </summary>
        private void SuspendNonRecursive()
        {
            Mailbox.Suspend();
        }

        /// <summary>
        ///     Watcheds the actor terminated.
        /// </summary>
        /// <param name="m">The m.</param>
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
            var actor = m.Actor;
            if(WatchingContains(actor))
            {
                watchees.Remove(actor);
                if (!isTerminating)
                {
                    //TODO: what params should be used for the bools?

                    Self.Tell(new Terminated(actor,true,false), actor);
                    TerminatedQueueFor(actor);
                }
            }
            if (children.ContainsKey(actor.Path.Name))
            {
                HandleChildTerminated(actor);
            }
        }

        private bool WatchingContains(ActorRef actor)
        {
            return watchees.Contains(actor);
        }

        private void TerminatedQueueFor(ActorRef actorRef)
        {
            terminatedQueue.Add(actorRef);
        }

        /// <summary>
        ///     Handles the child terminated.
        /// </summary>
        /// <param name="actor">The actor.</param>
        private void HandleChildTerminated(ActorRef actor)
        {
            InternalActorRef tmp;
            children.TryRemove(actor.Path.Name, out tmp);
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

        /// <summary>
        ///     Terminates this instance.
        /// </summary>
        private void Terminate()
        {
            if (isTerminating)
                return;

            isTerminating = true;
            _self.IsTerminated = true;

            UnwatchWatchedActors(_actor);
            foreach (InternalActorRef child in GetChildren())
            {
                child.Stop();
            }

            if (System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), ActorType, "stopping"));
            FinishTerminate();
        }

        /// <summary>
        ///     Finishes the terminate.
        /// </summary>
        private void FinishTerminate()
        {
            if (_actor == null)
            {
                //TODO: this is the root actor, do something....
                return;
            }

            ActorBase a = _actor;
            if (a != null)
            {
                try
                {
                    _actor.AroundPostStop();
                }
                catch (Exception x)
                {
                    HandleNonFatalOrInterruptedException(
                        () => Publish(new Error(x, Self.Path.ToString(), ActorType, x.Message)));
                }
            }
            var mailbox = Mailbox;
            Mailbox = System.Mailboxes.DeadLetterMailbox;
            mailbox.Stop();
            Parent.Tell(new DeathWatchNotification(Self, true, false));
            TellWatchersWeDied();
            UnwatchWatchedActors(a);
            if(System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), ActorType, "stopped"));

            ClearActor();
            ClearActorCell();
            _actor = null;
        }

        /// <summary>
        ///     Handles the non fatal or interrupted exception.
        /// </summary>
        /// <param name="action">The action.</param>
        private void HandleNonFatalOrInterruptedException(Action action)
        {
            try
            {
                action();
            }
            catch
            {
            }
        }

        /// <summary>
        ///     Publishes the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
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

        /// <summary>
        ///     Tries the catch.
        /// </summary>
        /// <param name="action">The action.</param>
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

        /// <summary>
        ///     Tells the watchers we died.
        /// </summary>
        private void TellWatchersWeDied()
        {
            WatchedBy.Tell(new DeathWatchNotification(Self, true, false));
        }

        /// <summary>
        ///     Unwatches the watched actors.
        /// </summary>
        /// <param name="actorBase">The actor base.</param>
        private void UnwatchWatchedActors(ActorBase actorBase)
        {
            try
            {
                foreach (ActorRef watchee in watchees)
                {
                    watchee.Tell(new Unwatch(watchee, Self));
                }
            }
            finally
            {
                watchees.Clear();
                terminatedQueue.Clear();
            }
        }


        private void Supervise(ActorRef child, bool async)
        {
            //TODO: complete this
    //  if (!isTerminating) {
    //    // Supervise is the first thing we get from a new child, so store away the UID for later use in handleFailure()
    //    initChild(child) match {
    //      case Some(crs) ⇒
    //        handleSupervise(child, async)
    //        if (system.settings.DebugLifecycle) publish(Debug(self.path.toString, clazz(actor), "now supervising " + child))
    //      case None ⇒ publish(Error(self.path.toString, clazz(actor), "received Supervise from unregistered child " + child + ", this will not end well"))
    //    }
    //  }
            HandleSupervise(child, async);
            if (System.Settings.DebugLifecycle)
            {
                Publish(new Debug(Self.Path.ToString(), ActorType, "now supervising " + child.Path));
            }
            //     case None ⇒ publish(Error(self.path.toString, clazz(actor), "received Supervise from unregistered child " + child + ", this will not end well"))
        }

        private void HandleSupervise(ActorRef child, bool async)
        {
            if(async && child is RepointableActorRef)
            {
                ((RepointableActorRef)child).Point();
            }
        }

        /// <summary>
        ///     Handles the identity.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleIdentity(Identify m)
        {
            Sender.Tell(new ActorIdentity(m.MessageId, Self));
        }

        /// <summary>
        ///     Handles the poison pill.
        /// </summary>
        private void HandlePoisonPill()
        {
            _self.Stop();
        }

        /// <summary>
        ///     Async restart this actor
        /// </summary>
        public void Restart()
        {
            Self.Tell(new Recreate(null));
        }

        /// <summary>
        ///     Restarts the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        public void Restart(Exception cause)
        {
            Self.Tell(new Recreate(cause));
        }


        private void HandleCreate(Create obj)   //Called create in Akka (ActorCell.scala)
        {
            //TODO: this is missing bits and pieces compared to Akka
            try
            {
                var instance = NewActor();
                _actor = instance;
                UseThreadContext(() => instance.AroundPreStart());
                if(System.Settings.DebugLifecycle)
                    Publish(new Debug(Self.Path.ToString(),instance.GetType(),"Started ("+instance+")"));
            }
            catch
            {                
                throw;
            }
        }

        /// <summary>
        ///     Faults the recreate.
        /// </summary>
        /// <param name="m">The m.</param>
        private void FaultRecreate(Recreate m)
        {
            isTerminating = false;
            ActorBase failedActor = _actor;

            object optionalMessage = CurrentMessage;

            if (System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), failedActor.GetType(), "restarting"));

            try
            {
                failedActor.AroundPreRestart(m.Cause, optionalMessage);
            }
            catch (Exception e)
            {
                HandleNonFatalOrInterruptedException(() =>
                {
                    var ex = new PreRestartException(_self, e, m.Cause, optionalMessage);
                    Publish(new Error(ex, Self.Path.ToString(), failedActor.GetType(), e.Message));
                });
            }

            var freshActor = NewActor();
            _actor = freshActor;
            UseThreadContext(() =>
            {
                Mailbox.Resume();
                freshActor.AroundPostRestart(m.Cause, null);
            });
            if(System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), freshActor.GetType(), "restarted (" + freshActor + ")"));
        }

        /// <summary>
        ///     Starts this instance.
        /// </summary>
        public void Start()
        {
            if (isTerminating)
                return;

            PreStart();
            Mailbox.Start();
        }

        /// <summary>
        /// Allow extra pre-start initialization in derived classes
        /// </summary>
        protected virtual void PreStart()
        {
        }

        /// <summary>
        ///     Resumes the specified caused by failure.
        /// </summary>
        /// <param name="causedByFailure">The caused by failure.</param>
        public void Resume(Exception causedByFailure)
        {
            if (isTerminating)
                return;

            Self.Tell(new Resume(causedByFailure));
        }

        /// <summary>
        ///     Async stop this actor
        /// </summary>
        public void Stop()
        {
            //try
            //{
            Self.Tell(Akka.Dispatch.SysMsg.Terminate.Instance);
            //}
            //catch
            //{
            //}
        }

        /// <summary>
        ///     Suspends this instance.
        /// </summary>
        public void Suspend()
        {
            if (isTerminating)
                return;

            Self.Tell(Akka.Dispatch.SysMsg.Suspend.Instance);
        }

        /// <summary>
        ///     Kills this instance.
        /// </summary>
        /// <exception cref="ActorKilledException">Kill</exception>
        private void Kill()
        {
            throw new ActorKilledException("Kill");
        }

        /// <summary>
        ///     Handles the failed.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleFailed(Failed m)
        {
            bool handled = _actor.SupervisorStrategyLazy().HandleFailure(this, m.Child, m.Cause);
            if (!handled)
                throw m.Cause;
        }

        /// <summary>
        ///     Handles the watch.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleWatch(Watch m)
        {
            var watchee = m.Watchee;    //The actor to watch
            var watcher = m.Watcher;    //The actor that watches
            var self = Self;
            var watcheeIsSelf = watchee == self;
            var watcherIsSelf = watcher == self;
            if(watcheeIsSelf && !watcherIsSelf) //Check if someone else is trying to watch us
            {
                if(WatchedBy.TryAdd(m.Watcher))
                {
                    //TODO: Missing call to maintainAddressTerminatedSubscription
                    if(System.Settings.DebugLifecycle)
                        Publish(new Debug(Self.Path.ToString(), ActorType, "now watched by " + m.Watcher));
                }
            }
            else if(!watcheeIsSelf && watcherIsSelf)  
            {
                Watch(watchee);
            }
            else
            {
                Publish(new Warning(self.Path.ToString(), ActorType, string.Format("BUG: illegal Watch({0},{1}) for {2}", watchee, watcher, self)));
            }
        }

        //TODO: find out why this is never called
        /// <summary>
        ///     Handles the unwatch.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleUnwatch(Unwatch m)
        {
            WatchedBy.Remove(m.Watcher);
            terminatedQueue.Remove(m.Watchee);
        }

        /// <summary>
        ///     Handles the complete future.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleCompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}