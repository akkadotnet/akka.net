using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch;
using Akka.Util.Internal;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// Abstract base class for stash support
    /// </summary>
    public abstract class AbstractStash : IStash, IStashSupport
    {
        protected IActorContext Context;
        protected ActorRef Self;
        protected LinkedList<Envelope> TheStash;
        protected ActorCell ActorCell;
        protected DequeBasedMailbox Mailbox;

        protected AbstractStash(IActorContext context, int capacity = 100)
        {
            var actorCell = context.AsInstanceOf<ActorCell>();
            var mailbox = actorCell.Mailbox as DequeBasedMailbox;
            if (mailbox == null)
            {
                string message = @"DequeBasedMailbox required, got: " + actorCell.Mailbox.GetType().Name +  @"
An (unbounded) deque-based mailbox can be configured as follows:
    my-custom-mailbox {
        mailbox-type = ""Akka.Dispatch.UnboundedDequeBasedMailbox""
    }";
                throw new NotSupportedException(message);
            }
            Mailbox = mailbox;
            Context = context;
            Self = Context.Self;
            TheStash = new LinkedList<Envelope>();
            ActorCell = actorCell;
            Capacity = capacity;
        }


        public void Stash()
        {
            StashInternal();
        }

        public void Unstash()
        {
            UnstashInternal();
        }

        public void UnstashAll()
        {
            UnstashAllInternal();
        }

        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            UnstashAllInternal(predicate);
        }

        /// <summary>
        /// TODO: capacity needs to come from dispatcher or mailbox confg
        /// https://github.com/akka/akka/blob/master/akka-actor/src/main/scala/akka/actor/Stash.scala#L126
        /// </summary>
        public int Capacity { get; protected set; }

        public void StashInternal()
        {
            var currMsg = ActorCell.CurrentMessage;
            var sender = ActorCell.Sender;
            if(TheStash.Count > 0 && TheStash.Last.Value.Message.Equals(currMsg) && TheStash.Last.Value.Sender == sender)
                throw new IllegalActorStateException(string.Format("Can't stash the same message {0} more than once", currMsg));
            if (Capacity <= 0 || TheStash.Count < Capacity)
                TheStash.AddLast(new Envelope() {Message = currMsg, Sender = sender});
            else throw new StashOverflowException(string.Format("Couldn't enqueue message {0} to stash of {1}", currMsg, Self));
        }

        public virtual void PrependInternal(IEnumerable<Envelope> others)
        {
            foreach (var other in others)
            {
                TheStash.AddFirst(other);
            }
        }

        public virtual void UnstashInternal()
        {
            if (TheStash.Count > 0)
            {
                try
                {
                    EnqueueFirst(TheStash.Head());
                }
                finally
                {
                    TheStash.RemoveFirst();
                }
            }
        }

        public virtual void UnstashAllInternal(Func<Envelope,bool> predicate)
        {
            if (TheStash.Count > 0)
            {
                try
                {
                    foreach (var item in TheStash.Where(predicate))
                    {
                        EnqueueFirst(item);
                    }
                }
                finally
                {
                    TheStash = new LinkedList<Envelope>();
                }
            }
        }

        public virtual void UnstashAllInternal()
        {
            UnstashAllInternal(envelope => true);
        }

        public virtual IEnumerable<Envelope> ClearStash()
        {
            var stashed = TheStash;
            TheStash = new LinkedList<Envelope>();
            return stashed;
        }

        public virtual void EnqueueFirst(Envelope msg)
        {
            Mailbox.EnqueueFirst(msg);
        }
    }
}