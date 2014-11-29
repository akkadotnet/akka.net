using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
using Akka.Util.Internal;
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
                if (autoReceivedMessage != null)
                    AutoReceiveMessage(envelope);
                else
                    ReceiveMessage(message);
            }
            catch (Exception cause)
            {
                HandleInvokeFailure(cause);
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

            try
            {
                envelope
                    .Message
                    .Match()
                    .With<CompleteTask>(HandleCompleteTask)
                    .With<Failed>(HandleFailed)
                    .With<DeathWatchNotification>(
                        m => WatchedActorTerminated(m.Actor, m.ExistenceConfirmed, m.AddressTerminated))
                    .With<Create>(m => HandleCreate(m.Failure))
                    //TODO: see """final def init(sendSupervise: Boolean, mailboxType: MailboxType): this.type = {""" in dispatch.scala
                    //case Create(failure) ⇒ create(failure)
                    .With<Watch>(m => AddWatcher(m.Watchee, m.Watcher))
                    .With<Unwatch>(m => RemWatcher(m.Watchee, m.Watcher))
                    .With<Recreate>(m => FaultRecreate(m.Cause))
                    .With<Suspend>(m => FaultSuspend())
                    .With<Resume>(m => FaultResume(m.CausedByFailure))
                    .With<Terminate>(Terminate)
                    .With<Supervise>(s => Supervise(s.Child, s.Async))
                    .Default(m => { throw new NotSupportedException("Unknown message " + m.GetType().Name); });
            }
            catch (Exception cause)
            {
                HandleInvokeFailure(cause);
            }
        }

        private void HandleCompleteTask(CompleteTask task)
        {
            CurrentMessage = task.State.Message;
            Sender = task.State.Sender;
            task.SetResult();
        }

        public void SwapMailbox(DeadLetterMailbox mailbox)
        {
            Mailbox.DebugPrint("{0} Swapping mailbox to DeadLetterMailbox",Self);
            Interlocked.Exchange(ref _mailbox, mailbox);
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
            if (!IsTerminating)
            {
                var childRestartStats = InitChild((InternalActorRef) child);
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
        ///     Restarts the specified cause.
        /// </summary>
        /// <param name="cause">The cause.</param>
        public void Restart(Exception cause)
        {
            SendSystemMessage(new Recreate(cause));
        }

        private void HandleCreate(Exception failure)
        {
            Create(failure);
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
        public void Start()
        {
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
            SendSystemMessage(new Resume(causedByFailure));
        }

        /// <summary>
        ///     Async stop this actor
        /// </summary>
        public void Stop()
        {
            SendSystemMessage(Dispatch.SysMsg.Terminate.Instance);
        }

        /// <summary>
        ///     Suspends this instance.
        /// </summary>
        public void Suspend()
        {
            SendSystemMessage(Dispatch.SysMsg.Suspend.Instance);
        }

        private void SendSystemMessage(SystemMessage systemMessage)
        {
            try
            {
                Self.Tell(systemMessage);
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