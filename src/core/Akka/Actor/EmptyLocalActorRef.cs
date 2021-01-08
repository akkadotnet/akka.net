//-----------------------------------------------------------------------
// <copyright file="EmptyLocalActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    /// <summary>
    /// TBD
    /// </summary>
    public class EmptyLocalActorRef : MinimalActorRef
    {
        private readonly IActorRefProvider _provider;
        private readonly ActorPath _path;
        private readonly EventStream _eventStream;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="provider">TBD</param>
        /// <param name="path">TBD</param>
        /// <param name="eventStream">TBD</param>
        public EmptyLocalActorRef(IActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            _provider = provider;
            _path = path;
            _eventStream = eventStream;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ActorPath Path { get { return _path; } }

        /// <summary>
        /// TBD
        /// </summary>
        public override IActorRefProvider Provider { get { return _provider; } }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete("Use Context.Watch and Receive<Terminated> [1.1.0]")]
        public override bool IsTerminated { get { return true; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <exception cref="InvalidMessageException">This exception is thrown if the given <paramref name="message"/> is undefined.</exception>
        protected override void TellInternal(object message, IActorRef sender)
        {
            if (message == null) throw new InvalidMessageException("Message is null");
            var d = message as DeadLetter;
            if (d != null) SpecialHandle(d.Message, d.Sender);
            else if (!SpecialHandle(message, sender))
            {
                _eventStream.Publish(new DeadLetter(message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public override void SendSystemMessage(ISystemMessage message)
        {
            Mailbox.DebugPrint("EmptyLocalActorRef {0} having enqueued {1}", Path, message);
            SpecialHandle(message, _provider.DeadLetters);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        /// <returns>TBD</returns>
        protected virtual bool SpecialHandle(object message, IActorRef sender)
        {
            var watch = message as Watch;
            if (watch != null)
            {
                if (watch.Watchee.Equals(this) && !watch.Watcher.Equals(this))
                {
                    watch.Watcher.SendSystemMessage(new DeathWatchNotification(watch.Watchee, existenceConfirmed: false, addressTerminated: false));
                }
                return true;
            }
            if (message is Unwatch)
                return true;    //Just ignore

            var identify = message as Identify;
            if (identify != null)
            {
                sender.Tell(new ActorIdentity(identify.MessageId, null));
                return true;
            }

            var actorSelectionMessage = message as ActorSelectionMessage;
            if (actorSelectionMessage != null)
            {
                var selectionIdentify = actorSelectionMessage.Message as Identify;
                if (selectionIdentify != null)
                {
                    if (!actorSelectionMessage.WildCardFanOut)
                        sender.Tell(new ActorIdentity(selectionIdentify.MessageId, null));
                }
                else
                {
                    _eventStream.Publish(new DeadLetter(actorSelectionMessage.Message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                }
                return true;
            }

            var deadLetterSuppression = message as IDeadLetterSuppression;
            if (deadLetterSuppression != null)
            {
                _eventStream.Publish(new SuppressedDeadLetter(deadLetterSuppression, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                return true;
            }

            return false;
        }
    }
}
