using Pigeon.Messaging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Actor
{
    public partial class ActorCell : ActorContext
    {
        internal Props Props { get;private set; }
        internal LocalActorRef Self { get;private set; }
        internal ActorContext Parent { get;private set; }
        internal ActorBase Actor { get; set; }
        private Message CurrentMessage { get; set; }
        internal ActorRef Sender { get;private set; }
        internal MessageHandler CurrentBehavior { get; private set; }
        private Stack<MessageHandler> behaviorStack = new Stack<MessageHandler>();
        private Mailbox Mailbox { get; set; }
        private HashSet<ActorRef> Watchees = new HashSet<ActorRef>();
        [ThreadStatic]
        private static ActorCell current;
        internal static ActorCell Current
        {
            get
            {
                return current;
            }
        }

        internal ActorCell(ActorContext parent, Props props, string name)
        {
            this.Parent = parent;
            this.System = parent.System;
            this.Self = new LocalActorRef(new ActorPath(name), this);
            this.Props = props;
            this.Mailbox = new BufferBlockMailbox();
            this.Mailbox.OnNext = this.OnNext;
        }

        internal void UseThreadContext(Action action)
        {
            var tmp = Current;
            current = this;
            try
            {
                action();
            }
            finally
            {
                //ensure we set back the old context
                current = tmp;
            }
        }


        public void Become(MessageHandler receive)
        {
            behaviorStack.Push(receive);
            CurrentBehavior = receive;
        }
        public void Unbecome()
        {
            CurrentBehavior = behaviorStack.Pop(); ;
        }
        public void OnNext(Message message)
        {
            this.CurrentMessage = message;
            this.Sender = message.Sender;
            //set the current context
            UseThreadContext(() =>
            {
                OnReceiveInternal(message.Payload);
            });
        }
        internal void Post(ActorRef sender, LocalActorRef target, object message)
        {
            var m = new Message
            {
                Sender = sender,
                Target = target,
                Payload = message,
            };
            Mailbox.Post(m);
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="subject"></param>
        public void Watch(ActorRef subject)
        {
            Watchees.Add(subject);
            subject.Tell(new Watch());
        }

        /// <summary>
        /// May only be called from the owner actor
        /// </summary>
        /// <param name="subject"></param>
        public void Unwatch(ActorRef subject)
        {
            Watchees.Remove(subject);
            subject.Tell(new Unwatch());
        }
    }
}
