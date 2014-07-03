using System;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;

namespace Akka.Actor
{
    public class EmptyLocalActorRef : MinimalActorRef
    {
        private readonly ActorRefProvider _provider;
        private readonly ActorPath _path;
        private readonly EventStream _eventStream;

        public EmptyLocalActorRef(ActorRefProvider provider, ActorPath path, EventStream eventStream)
        {
            _provider = provider;
            _path = path;
            _eventStream = eventStream;
        }

        public override ActorPath Path { get { return _path; } }

        public override ActorRefProvider Provider { get { return _provider; } }

        protected override void TellInternal(object message, ActorRef sender)
        {
            var systemMessage = message as SystemMessage;
            if(systemMessage != null)
            {
                SendSystemMessage(systemMessage);
                return;
            }
            SendUserMessage(message, sender);
        }

        protected virtual void SendUserMessage(object message, ActorRef sender)
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

        private void SendSystemMessage(SystemMessage message)
        {
            if(Mailbox.Debug) Console.WriteLine("EmptyLocalActorRef {0} having enqueued {1}", Path, message);
            SpecialHandle(message, _provider.DeadLetters);
        }

        protected virtual bool SpecialHandle(object message, ActorRef sender)
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