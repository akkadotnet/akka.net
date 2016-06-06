//-----------------------------------------------------------------------
// <copyright file="EmptyLocalActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    public class EmptyLocalActorRef : MinimalActorRef
    {
        private readonly IActorRefProvider _provider;
        private readonly ActorPath _path;
        private readonly EventStream _eventStream;

        public EmptyLocalActorRef(IActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            _provider = provider;
            _path = path;
            _eventStream = eventStream;
        }

        public override ActorPath Path { get { return _path; } }

        public override IActorRefProvider Provider { get { return _provider; } }

        [Obsolete("Use Context.Watch and Receive<Terminated>")]
        public override bool IsTerminated { get { return true; } }

        protected override void TellInternal(object message, IActorRef sender)
        {
            if(message == null) throw new InvalidMessageException("Message is null");
            var d = message as DeadLetter;
            if (d != null) SpecialHandle(d.Message, d.Sender);
            else if (!SpecialHandle(message, sender))
            {
                _eventStream.Publish(new DeadLetter(message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
            }
        }

        public override void SendSystemMessage(ISystemMessage message)
        {
            Mailbox.DebugPrint("EmptyLocalActorRef {0} having enqueued {1}", Path, message);
            SpecialHandle(message, _provider.DeadLetters);
        }

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
                    if(!actorSelectionMessage.WildCardFanOut)
                        sender.Tell(new ActorIdentity(selectionIdentify.MessageId, null));
                }
                else
                {
                    _eventStream.Publish(new DeadLetter(actorSelectionMessage.Message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                }
                return true;
            }

            // TODO: DeadLetterSupression

            return false;
        }
    }
}

