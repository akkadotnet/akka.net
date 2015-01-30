using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch;
using Akka.Util.Internal;

namespace Akka.Actor.Internal
{
    /// <summary>INTERNAL
    /// Abstract base class for stash support
    /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
    /// </summary>
    public abstract class AbstractStash : IStash
    {
        private LinkedList<Envelope> _theStash;
        private readonly ActorCell _actorCell;
        private readonly int _capacity;

        /// <summary>INTERNAL
        /// Abstract base class for stash support
        /// <remarks>Note! Part of internal API. Breaking changes may occur without notice. Use at own risk.</remarks>
        /// </summary>
        protected AbstractStash(IActorContext context, int capacity = 100)
        {
            var actorCell = (ActorCell)context;
            var mailbox = actorCell.Mailbox as DequeBasedMailbox;
            if(mailbox == null)
            {
                string message = @"DequeBasedMailbox required, got: " + actorCell.Mailbox.GetType().Name + @"
An (unbounded) deque-based mailbox can be configured as follows:
    my-custom-mailbox {
        mailbox-type = ""Akka.Dispatch.UnboundedDequeBasedMailbox""
    }";
                throw new NotSupportedException(message);
            }
            _theStash = new LinkedList<Envelope>();
            _actorCell = actorCell;

            // TODO: capacity needs to come from dispatcher or mailbox confg
            // https://github.com/akka/akka/blob/master/akka-actor/src/main/scala/akka/actor/Stash.scala#L126
            _capacity = capacity;
        }

        private DequeBasedMailbox Mailbox { get { return (DequeBasedMailbox)_actorCell.Mailbox; } }

        public void Stash()
        {
            var currMsg = _actorCell.CurrentMessage;
            var sender = _actorCell.Sender;
            if(_theStash.Count > 0)
            {
                var lastEnvelope = _theStash.Last.Value;
                if(lastEnvelope.Message.Equals(currMsg) && lastEnvelope.Sender == sender)
                    throw new IllegalActorStateException(string.Format("Can't stash the same message {0} more than once", currMsg));
            }
            if(_capacity <= 0 || _theStash.Count < _capacity)
                _theStash.AddLast(new Envelope() { Message = currMsg, Sender = sender });
            else throw new StashOverflowException(string.Format("Couldn't enqueue message {0} to stash of {1}", currMsg, _actorCell.Self));
        }

        public void Unstash()
        {
            if(_theStash.Count > 0)
            {
                try
                {
                    EnqueueFirst(_theStash.Head());
                }
                finally
                {
                    _theStash.RemoveFirst();
                }
            }
        }

        public void UnstashAll()
        {
            UnstashAll(envelope => true);
        }

        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            if(_theStash.Count > 0)
            {
                try
                {
                    foreach(var item in _theStash.Where(predicate))
                    {
                        EnqueueFirst(item);
                    }
                }
                finally
                {
                    _theStash = new LinkedList<Envelope>();
                }
            }
        }
     
        public IEnumerable<Envelope> ClearStash()
        {
            if(_theStash.Count == 0)
                return Enumerable.Empty<Envelope>();

            var stashed = _theStash;
            _theStash = new LinkedList<Envelope>();
            return stashed;
        }

        public void Prepend(IEnumerable<Envelope> envelopes)
        {
            // since we want to save the order of messages, but still prepending using AddFirst,
            // we must enumerate envelopes in reversed order
            foreach (var envelope in envelopes.Distinct().Reverse())
            {
                _theStash.AddFirst(envelope);
            }
        }

        /// <summary>
        /// Enqueues <paramref name="msg"/> at the first position in the mailbox. If the message contained in
        /// the envelope is a <see cref="Terminated"/> message, it will be ensured that it can be re-received
        /// by the actor.
        /// </summary>
        private void EnqueueFirst(Envelope msg)
        {
            Mailbox.EnqueueFirst(msg);
            var terminatedMessage = msg.Message as Terminated;
            if(terminatedMessage != null)
            {
                _actorCell.TerminatedQueuedFor(terminatedMessage.ActorRef);
            }
        }
    }
}

