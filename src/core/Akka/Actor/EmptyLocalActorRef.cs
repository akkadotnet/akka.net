//-----------------------------------------------------------------------
// <copyright file="EmptyLocalActorRef.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
        public override bool IsTerminated { get { return true; } }

        protected override void TellInternal(object message, IActorRef sender)
        {
            var systemMessage = message as ISystemMessage;
            if(systemMessage != null)
            {
                SendSystemMessage(systemMessage);
                return;
            }
            SendUserMessage(message, sender);
        }

        protected virtual void SendUserMessage(object message, IActorRef sender)
        {
            if(message == null) throw new InvalidMessageException();
            var deadLetter = message as DeadLetter;
            if(deadLetter != null)
                HandleDeadLetter(deadLetter);
            else
            {
                var wasHandled = SpecialHandle(message, sender);
                if(!wasHandled)
                {
                    _eventStream.Publish(new DeadLetter(message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                }
            }
        }

        protected virtual void HandleDeadLetter(DeadLetter deadLetter)
        {
            SpecialHandle(deadLetter.Message, deadLetter.Sender);
        }

        private void SendSystemMessage(ISystemMessage message)
        {
            Mailbox.DebugPrint("EmptyLocalActorRef {0} having enqueued {1}", Path, message);
            SpecialHandle(message, _provider.DeadLetters);
        }

        protected virtual bool SpecialHandle(object message, IActorRef sender)
        {
            var w = message as Watch;
            if(w != null)
            {
                if(w.Watchee == this && w.Watcher != this)
                {
                    w.Watcher.Tell(new DeathWatchNotification(w.Watchee, existenceConfirmed: false, addressTerminated: false));
                }
                return true;
            }
            if(message is Unwatch)
                return true;    //Just ignore
            var identify = message as Identify;
            if(identify != null)
            {
                sender.Tell(new ActorIdentity(identify.MessageId, null));
                return true;
            }
            var sel = message as ActorSelectionMessage;
            if(sel != null)
            {
                var selectionIdentify = sel.Message as Identify;
                if(selectionIdentify != null)
                {
                    if(!sel.WildCardFanOut)
                        sender.Tell(new ActorIdentity(selectionIdentify.MessageId, null));
                }
                else
                {
                    _eventStream.Publish(new DeadLetter(sel.Message, sender.IsNobody() ? _provider.DeadLetters : sender, this));
                }
                return true;
            }
            return false;
        }
    }
}

