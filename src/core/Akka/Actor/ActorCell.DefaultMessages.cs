//-----------------------------------------------------------------------
// <copyright file="ActorCell.DefaultMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Debug = Akka.Event.Debug;

namespace Akka.Actor
{
    /// <summary>
    ///     Class ActorCell.
    /// </summary>
    [DebuggerDisplay("{Self,nq}")]
    public partial class ActorCell
    {
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

        private int _currentEnvelopeId;

        public int CurrentEnvelopeId
        {
            get { return _currentEnvelopeId; }
        }
        /// <summary>
        ///     Invokes the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        public void Invoke(Envelope envelope)
        {
            
            var message = envelope.Message;
            var influenceReceiveTimeout = !(message is INotInfluenceReceiveTimeout);

            try
            {
                // Akka JVM doesn't have these lines
                CurrentMessage = envelope.Message;
                _currentEnvelopeId++;

                Sender = MatchSender(envelope);

                if (influenceReceiveTimeout)
                {
                    CancelReceiveTimeout();
                }

                if (message is IAutoReceivedMessage)
                {
                    AutoReceiveMessage(envelope);
                }
                else
                {
                    ReceiveMessage(message);
                }
                CurrentMessage = null;
            }
            catch (Exception cause)
            {
                HandleInvokeFailure(cause);
            }
            finally
            {
                if (influenceReceiveTimeout)
                {
                    CheckReceiveTimeout(); // Reschedule receive timeout
                }
            }
        }

        /// <summary>
        /// If the envelope.Sender property is null, then we'll substitute
        /// Deadletters as the <see cref="Sender"/> of this message.
        /// </summary>
        /// <param name="envelope">The envelope we received</param>
        /// <returns>An IActorRef that corresponds to a Sender</returns>
        private IActorRef MatchSender(Envelope envelope)
        {
            var sender = envelope.Sender;
            return sender ?? System.DeadLetters;
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

            if (System.Settings.DebugAutoReceive)
                Publish(new Debug(Self.Path.ToString(), actorType, "received AutoReceiveMessage " + message));

            var m = envelope.Message;
            if (m is Terminated) ReceivedTerminated(m as Terminated);
            else if (m is AddressTerminated) AddressTerminated((m as AddressTerminated).Address);
            else if (m is Kill) Kill();
            else if (m is PoisonPill) HandlePoisonPill();
            else if (m is ActorSelectionMessage) ReceiveSelection(m as ActorSelectionMessage);
            else if (m is Identify) HandleIdentity(m as Identify);
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
            var wasHandled = _actor.AroundReceive(_state.GetCurrentBehavior(), message);

            if (System.Settings.AddLoggingReceive && _actor is ILogReceive)
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

        /* SystemMessage handling */

        private int CalculateState()
        {
            if(IsWaitingForChildren) return SuspendedWaitForChildrenState;
            if(Mailbox.IsSuspended()) return SuspendedState;
            return DefaultState;
        }

        private static bool ShouldStash(SystemMessage m, int state)
        {
            switch (state)
            {
                case SuspendedWaitForChildrenState:
                    return m is IStashWhenWaitingForChildren;
                case SuspendedState:
                    return m is IStashWhenFailed;
                case DefaultState:
                default:
                    return false;
            }
        }

        private void SendAllToDeadLetters(EarliestFirstSystemMessageList messages)
        {
            if (messages.IsEmpty) return; // don't run any of this if there are no system messages
            do
            {
                var msg = messages.Head;
                messages = messages.Tail;
                msg.Unlink();
                SystemImpl.Provider.DeadLetters.Tell(msg, Self);
            } while (messages.NonEmpty);
        }

        private void SysMsgInvokeAll(EarliestFirstSystemMessageList messages, int currentState)
        {
           
            var nextState = currentState;
            var todo = messages;
            while(true)
            {
                var m = todo.Head;
                todo = messages.Tail;
                m.Unlink();
                try
                {
                    // TODO: replace with direct cast
                    if (ShouldStash(m, nextState))
                    {
                        Stash(m);
                    }
                    if (m is ActorTaskSchedulerMessage) HandleActorTaskSchedulerMessage((ActorTaskSchedulerMessage)m);
                    else if (m is Failed) HandleFailed(m as Failed);
                    else if (m is DeathWatchNotification)
                    {
                        var msg = m as DeathWatchNotification;
                        WatchedActorTerminated(msg.Actor, msg.ExistenceConfirmed, msg.AddressTerminated);
                    }
                    else if (m is Create) Create((m as Create).Failure);
                    else if (m is Watch)
                    {
                        var watch = m as Watch;
                        AddWatcher(watch.Watchee, watch.Watcher);
                    }
                    else if (m is Unwatch)
                    {
                        var unwatch = m as Unwatch;
                        RemWatcher(unwatch.Watchee, unwatch.Watcher);
                    }
                    else if (m is Recreate) FaultRecreate((m as Recreate).Cause);
                    else if (m is Suspend) FaultSuspend();
                    else if (m is Resume) FaultResume((m as Resume).CausedByFailure);
                    else if (m is Terminate) Terminate();
                    else if (m is Supervise)
                    {
                        var supervise = m as Supervise;
                        Supervise(supervise.Child, supervise.Async);
                    }
                    else
                    {
                        throw new NotSupportedException("Unknown message " + m.GetType().Name);
                    }
                }
                catch (Exception cause)
                {
                    HandleInvokeFailure(cause);
                }

                nextState = CalculateState();
                // As each state accepts a strict subset of another state, it is enough to unstash if we "walk up" the state
                // chain
                todo = nextState < currentState ? todo + UnstashAll() : todo;
                if (IsTerminated)
                {
                    SendAllToDeadLetters(todo);
                    return;
                }
                if (todo.IsEmpty) return; // keep running until the stash is empty
            }

        }

        /// <summary>
        ///   Used to invoke system messages.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        internal void SystemInvoke(ISystemMessage envelope)
        {
           SysMsgInvokeAll(new EarliestFirstSystemMessageList((SystemMessage)envelope), CalculateState());
        }

        private void HandleActorTaskSchedulerMessage(ActorTaskSchedulerMessage m)
        {
            //set the current message captured in the async operation
            //current message was cleared earlier when the async receive handler completed
            CurrentMessage = m.Message;
            if (m.Exception != null)
            {
                HandleInvokeFailure(m.Exception);
                return;
            }

            m.ExecuteTask();
            CurrentMessage = null;
        }

        internal Mailbox SwapMailbox(Mailbox mailbox)
        {
            Mailbox.DebugPrint("{0} Swapping mailbox to {1}", Self, mailbox);
            var ret = _mailboxDoNotCallMeDirectly;
#pragma warning disable 420
            Interlocked.Exchange(ref _mailboxDoNotCallMeDirectly, mailbox);
#pragma warning restore 420
            return ret;
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

        private void Supervise(IActorRef child, bool async)
        {
            //TODO: complete this
            if (!IsTerminating)
            {
                var childRestartStats = InitChild((IInternalActorRef)child);
                if (childRestartStats != null)
                {
                    HandleSupervise(child, async);
                    if (System.Settings.DebugLifecycle)
                    {
                        Publish(new Debug(Self.Path.ToString(), ActorType, "now supervising " + child.Path));
                    }
                }
                else
                {
                    Publish(new Debug(Self.Path.ToString(), ActorType, "received Supervise from unregistered child " + child.Path + ", this will not end well"));
                }
            }
        }

        private void HandleSupervise(IActorRef child, bool async)
        {
            if (async && child is RepointableActorRef)
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
        ///     Restarts the specified cause.
        /// </summary>
        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        /// <param name="cause">The cause.</param>
        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        private void Create(Exception failure)
        {
            if (failure != null)
                throw failure;
            try
            {
                var created = NewActor();
                _actor = created;
                UseThreadContext(() => created.AroundPreStart());
                CheckReceiveTimeout();
                if (System.Settings.DebugLifecycle)
                    Publish(new Debug(Self.Path.ToString(), created.GetType(), "Started (" + created + ")"));
            }
            catch (Exception e)
            {
                if (_actor != null)
                {
                    ClearActor(_actor);
                    _actor = null; // ensure that we know that we failed during creation
                }
                throw new ActorInitializationException(_self, "Exception during creation", e);
            }
        }

        /// <summary>
        ///     Starts this instance.
        /// </summary>
        public virtual void Start()
        {
            // This call is expected to start off the actor by scheduling its mailbox.
            Dispatcher.Attach(this);
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
        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        /// <param name="causedByFailure">The caused by failure.</param>
        public void Resume(Exception causedByFailure)
        {
            SendSystemMessage(new Resume(causedByFailure));
        }

        /// <summary>
        ///     Async stop this actor
        /// </summary>
        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        public void Stop()
        {
            SendSystemMessage(new Dispatch.SysMsg.Terminate());
        }

        /// <summary>
        ///     Suspends this instance.
        /// </summary>
        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        public void Suspend()
        {
            SendSystemMessage(new Dispatch.SysMsg.Suspend());
        }

        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        public void SendSystemMessage(ISystemMessage systemMessage)
        {
            try
            {
                Dispatcher.SystemDispatch(this, (SystemMessage)systemMessage);
            }
            catch (Exception e)
            {
                _systemImpl.EventStream.Publish(new Error(e, _self.Parent.ToString(), ActorType, "Swallowing exception during message send"));
            }
        }

        /// <summary>
        ///     Kills this instance.
        /// </summary>
        /// <exception cref="ActorKilledException">Kill</exception>
        private void Kill()
        {
            throw new ActorKilledException("Kill");
        }
    }
}

