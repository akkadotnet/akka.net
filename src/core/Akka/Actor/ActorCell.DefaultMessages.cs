//-----------------------------------------------------------------------
// <copyright file="ActorCell.DefaultMessages.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using System.Globalization;

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

        /// <summary>
        /// TBD
        /// </summary>
        public int CurrentEnvelopeId
        {
            get { return _currentEnvelopeId; }
        }
        /// <summary>
        ///     Invokes the specified envelope.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        /// <exception cref="ActorKilledException">
        /// This exception is thrown if a <see cref="Akka.Actor.Kill"/> message is included in the given <paramref name="envelope"/>.
        /// </exception>>
        public void Invoke(Envelope envelope)
        {

            var message = envelope.Message;
            var influenceReceiveTimeout = !(message is INotInfluenceReceiveTimeout);

            try
            {
                // Akka JVM doesn't have these lines
                CurrentMessage = envelope.Message;
                _currentEnvelopeId++;
                if (_currentEnvelopeId == int.MaxValue) _currentEnvelopeId = 0;

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
                // Schedule or reschedule receive timeout
                CheckReceiveTimeout(influenceReceiveTimeout);
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelope">TBD</param>
        /// <exception cref="ActorKilledException">
        /// This exception is thrown if a <see cref="Akka.Actor.Kill"/> message is included in the given <paramref name="envelope"/>.
        /// </exception>
        protected internal virtual void AutoReceiveMessage(Envelope envelope)
        {
            var message = envelope.Message;

            var actor = _actor;
            var actorType = actor != null ? actor.GetType() : null;

            if (System.Settings.DebugAutoReceive)
                Publish(new Debug(Self.Path.ToString(), actorType, "received AutoReceiveMessage " + message));

            var m = envelope.Message;
            switch (m)
            {
                case Terminated terminated:
                    ReceivedTerminated(terminated);
                    break;
                case AddressTerminated terminated:
                    AddressTerminated(terminated.Address);
                    break;
                case Kill _:
                    Kill();
                    break;
                case PoisonPill _:
                    HandlePoisonPill();
                    break;
                case ActorSelectionMessage selectionMessage:
                    ReceiveSelection(selectionMessage);
                    break;
                case Identify identify:
                    HandleIdentity(identify);
                    break;
            }
        }

        /// <summary>
        /// This is only intended to be called from TestKit's TestActorRef
        /// </summary>
        /// <param name="envelope">TBD</param>
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

        /// <summary>
        /// Receives the next message from the mailbox and feeds it to the underlying actor instance.
        /// </summary>
        /// <param name="message">The message that will be sent to the actor.</param>
        protected virtual void ReceiveMessage(object message)
        {
            var wasHandled = _actor.AroundReceive(_state.GetCurrentBehavior(), message);

            if (System.Settings.AddLoggingReceive && _actor is ILogReceive)
            {
                //TODO: akka alters the receive handler for logging, but the effect is the same. keep it this way?
                var msg = "received " + (wasHandled ? "handled" : "unhandled") + " message " + message + " from " + Sender.Path;
                Publish(new Debug(Self.Path.ToString(), _actor.GetType(), msg));
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
            while (true)
            {
                var rest = messages.Tail;
                var message = messages.Head;
                message.Unlink();

                try
                {
                    switch (message)
                    {
                        case SystemMessage sm when ShouldStash(sm, currentState):
                            Stash(message);
                            break;
                        case ActorTaskSchedulerMessage atsm:
                            HandleActorTaskSchedulerMessage(atsm);
                            break;
                        case Failed f:
                            HandleFailed(f);
                            break;
                        case DeathWatchNotification n:
                            WatchedActorTerminated(n.Actor, n.ExistenceConfirmed, n.AddressTerminated);
                            break;
                        case Create c:
                            Create(c.Failure);
                            break;
                        case Watch w:
                            AddWatcher(w.Watchee, w.Watcher);
                            break;
                        case Unwatch uw:
                            RemWatcher(uw.Watchee, uw.Watcher);
                            break;
                        case Recreate r:
                            FaultRecreate(r.Cause);
                            break;
                        case Suspend _:
                            FaultSuspend();
                            break;
                        case Resume r:
                            FaultResume(r.CausedByFailure);
                            break;
                        case Terminate _:
                            Terminate();
                            break;
                        case Supervise s:
                            Supervise(s.Child, s.Async);
                            break;
                        default:
                            throw new NotSupportedException($"Unknown message {message.GetType().Name}");
                    }
                }
                catch (Exception cause)
                {
                    HandleInvokeFailure(cause);
                }

                var nextState = CalculateState();
                // As each state accepts a strict subset of another state, it is enough to unstash if we "walk up" the state
                // chain
                var todo = nextState < currentState ? rest + UnstashAll() : rest;

                if (IsTerminated)
                {
                    SendAllToDeadLetters(todo);
                    break;
                }
                else if (todo.IsEmpty) break;

                currentState = nextState;
                messages = todo;
            }
        }

        /// <summary>
        ///   Used to invoke system messages.
        /// </summary>
        /// <param name="envelope">The envelope.</param>
        /// <exception cref="ActorInitializationException">
        /// This exception is thrown if a <see cref="Akka.Dispatch.SysMsg.Create"/> system message is included in the given <paramref name="envelope"/>.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// This exception is thrown if an unknown message type is included in the given <paramref name="envelope"/>.
        /// </exception>
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
                Resume(m.Exception);
                HandleInvokeFailure(m.Exception);
                return;
            }

            m.ExecuteTask();
            CurrentMessage = null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="mailbox">TBD</param>
        /// <returns>TBD</returns>
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
            if (async && child is RepointableActorRef @ref)
            {
                @ref.Point();
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <remarks>➡➡➡ NEVER SEND THE SAME SYSTEM MESSAGE OBJECT TO TWO ACTORS ⬅⬅⬅</remarks>
        /// <param name="systemMessage">TBD</param>
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

        private void Kill()
        {
            throw new ActorKilledException("Kill");
        }
    }
}

