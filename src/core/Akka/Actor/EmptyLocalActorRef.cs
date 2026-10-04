//-----------------------------------------------------------------------
// <copyright file="EmptyLocalActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// Represents a local actor path that has no live actor and routes ordinary messages to dead letters.
    /// </summary>
    public class EmptyLocalActorRef : MinimalActorRef
    {
        private readonly IActorRefProvider _provider;
        private readonly ActorPath _path;
        private readonly EventStream _eventStream;

        /// <summary>
        /// Creates an empty reference for the specified path.
        /// </summary>
        /// <param name="provider">The provider used for dead letters and actor resolution.</param>
        /// <param name="path">The path whose actor is absent.</param>
        /// <param name="eventStream">The event stream used to publish dead letters.</param>
        public EmptyLocalActorRef(IActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            _provider = provider;
            _path = path;
            _eventStream = eventStream;
        }

        /// <summary>
        /// The path for which no live actor exists.
        /// </summary>
        public override ActorPath Path { get { return _path; } }

        /// <summary>
        /// The provider that owns this reference.
        /// </summary>
        public override IActorRefProvider Provider { get { return _provider; } }        

        /// <summary>
        /// Always <c>true</c>, because this reference does not represent a live actor.
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated> [1.1.0]")]
#pragma warning disable CS0809
        public override bool IsTerminated { get { return true; } }
#pragma warning restore CS0809
        /// <summary>
        /// Handles messages sent to this absent actor path.
        /// </summary>
        /// <param name="message">The message to deliver or publish as a dead letter.</param>
        /// <param name="sender">The message sender.</param>
        /// <exception cref="InvalidMessageException">This exception is thrown if the given <paramref name="message"/> is undefined.</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (message == null) throw new InvalidMessageException("Message is null");
            if (message is DeadLetter d) SpecialHandle(d.Message, d.Sender);
            else if (!SpecialHandle(message, sender))
            {
                _eventStream.Publish(new DeadLetter(message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
            }
        }

        /// <summary>
        /// Handles an incoming system message and supported control messages.
        /// </summary>
        /// <param name="message">The system message to handle.</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            Mailbox.DebugPrint("EmptyLocalActorRef {0} having enqueued {1}", Path, message);
            SpecialHandle(message, _provider.DeadLetters);
        }

        /// <summary>
        /// Handles control messages that can be answered without a live actor, and publishes other messages as dead letters.
        /// </summary>
        /// <param name="message">The message to handle.</param>
        /// <param name="sender">The sender that should receive an identity response, when applicable.</param>
        /// <returns><c>true</c> if the message was handled; otherwise, <c>false</c> so the caller can publish it as a dead letter.</returns>
        protected virtual bool SpecialHandle(object message, IActorRef sender)
        {
            if (message is Watch watch)
            {
                if (watch.Watchee.Equals(this) && !watch.Watcher.Equals(this))
                {
                    watch.Watcher.SendSystemMessage(new DeathWatchNotification(watch.Watchee, existenceConfirmed: false, addressTerminated: false));
                }
                return true;
            }
            if (message is Unwatch)
                return true;    //Just ignore

            if (message is Identify identify)
            {
                sender.Tell(new ActorIdentity(identify.MessageId, null));
                return true;
            }

            if (message is ActorSelectionMessage actorSelectionMessage)
            {
                if (actorSelectionMessage.Message is Identify selectionIdentify)
                {
                    if (!actorSelectionMessage.WildCardFanOut)
                        sender.Tell(new ActorIdentity(selectionIdentify.MessageId, null));
                }
                else
                {
                    if (WrappedMessage.IsDeadLetterSuppressedAnywhere(actorSelectionMessage.Message))
                    {
                        PublishSupressedDeadLetter(actorSelectionMessage.Message, sender);
                    }
                    else
                    {
                        _eventStream.Publish(new DeadLetter(actorSelectionMessage.Message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                    }
                }
                return true;
            }

            if (WrappedMessage.IsDeadLetterSuppressedAnywhere(message))
            {
                PublishSupressedDeadLetter(message, sender);
                return true;
            }

            return false;
        }

        private void PublishSupressedDeadLetter(object msg, IActorRef sender)
        {
            _eventStream.Publish(new SuppressedDeadLetter(msg, sender.IsNobody() ? _provider.DeadLetters : sender, this));
        }
    }
}
