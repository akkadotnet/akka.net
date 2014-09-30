using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
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
            finally
            {
                CheckReceiveTimeout(); // Reschedule receive timeout
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
                .With<AddressTerminated>(a => AddressTerminated(a.Address))
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
                        .With<DeathWatchNotification>(m => WatchedActorTerminated(m.Actor, m.ExistenceConfirmed, m.AddressTerminated))
                        .With<Create>(HandleCreate)
                        //TODO: see """final def init(sendSupervise: Boolean, mailboxType: MailboxType): this.type = {""" in dispatch.scala
                        //case Create(failure) ⇒ create(failure)
                        .With<Watch>(m => AddWatcher(m.Watchee, m.Watcher))
                        .With<Unwatch>(m => RemWatcher(m.Watchee, m.Watcher))
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
            foreach (var child in GetChildren())
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

            if (_actor != null)
            {
                try
                {
                    _actor.AroundPostStop();

                    //Check if the actor uses a stash. If it does we must Unstash all messages. 
                    //If the user do not want this behavior, the stash should be cleared in PostStop
                    //either by calling ClearStash or by calling UnstashAll.
                    var actorStash = _actor as IActorStash;
                    if(actorStash != null)
                    {
                        actorStash.Stash.UnstashAll();
                    }
                }
                catch (Exception x)
                {
                    HandleNonFatalOrInterruptedException(
                        () => Publish(new Error(x, Self.Path.ToString(), ActorType, x.Message)));
                }
            }
            //TODO: Akka Jvm: this is done in a call to dispatcher.detach()
            {
                //TODO: Akka Jvm: this is done in a call to MessageDispatcher.detach()
                {
                    var mailbox = Mailbox;
                    var deadLetterMailbox = System.Mailboxes.DeadLetterMailbox;
                    SwapMailbox(deadLetterMailbox);
                    mailbox.BecomeClosed();
                    mailbox.CleanUp();
                }
            }
            Parent.Tell(new DeathWatchNotification(Self, true, false));
            TellWatchersWeDied();
            UnwatchWatchedActors(_actor);
            if(System.Settings.DebugLifecycle)
                Publish(new Debug(Self.Path.ToString(), ActorType, "stopped"));

            ClearActor();
            ClearActorCell();
            _actor = null;
        }

        public void SwapMailbox(DeadLetterMailbox mailbox)
        {
            Mailbox.DebugPrint("{0} Swapping mailbox to DeadLetterMailbox",Self);
            Interlocked.Exchange(ref _mailbox, mailbox);
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
                //TODO: Hmmm?
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
                //TODO: Hmmm?
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
                CheckReceiveTimeout();
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

                //Check if the actor uses a stash. If it does we must Unstash all messages. 
                //If the user do not want this behavior, the stash should be cleared in PreRestart
                //either by calling ClearStash or by calling UnstashAll.
                var actorStash = failedActor as IActorStash;
                if(actorStash != null)
                {
                    actorStash.Stash.UnstashAll();
                }
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
        ///     Handles the complete future.
        /// </summary>
        /// <param name="m">The m.</param>
        private void HandleCompleteFuture(CompleteFuture m)
        {
            m.SetResult();
        }
    }
}