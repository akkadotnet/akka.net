//-----------------------------------------------------------------------
// <copyright file="AbstractStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
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
            var mailbox = actorCell.Mailbox as IDequeBasedMailbox;
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

            // TODO: capacity needs to come from dispatcher or mailbox config
            // https://github.com/akka/akka/blob/master/akka-actor/src/main/scala/akka/actor/Stash.scala#L126
            _capacity = capacity;
        }

        private IDequeBasedMailbox Mailbox { get { return (IDequeBasedMailbox)_actorCell.Mailbox; } }

        private int _currentEnvelopeId;

        /// <summary>
        /// Stashes the current message in the actor's state.
        /// </summary>
        /// <exception cref="IllegalActorStateException">Thrown if we attempt to stash the same message more than once.</exception>
        /// <exception cref="StashOverflowException">Thrown in the event that we're using a <see cref="BoundedMessageQueue"/> 
        /// for the <see cref="IStash"/> and we've exceeded capacity.</exception>
        public void Stash()
        {
            var currMsg = _actorCell.CurrentMessage;
            var sender = _actorCell.Sender;

            if (_actorCell.CurrentEnvelopeId == _currentEnvelopeId)
            {
                 throw new IllegalActorStateException(string.Format("Can't stash the same message {0} more than once", currMsg));
            }
            _currentEnvelopeId = _actorCell.CurrentEnvelopeId;
            
            if(_capacity <= 0 || _theStash.Count < _capacity)
                _theStash.AddLast(new Envelope() { Message = currMsg, Sender = sender });
            else throw new StashOverflowException(string.Format("Couldn't enqueue message {0} to stash of {1}", currMsg, _actorCell.Self));
        }

        /// <summary>
        /// Unstash the most recently stashed message (top of the message stack.)
        /// </summary>
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

        /// <summary>
        /// Unstash all of the <see cref="Envelope"/>s in the Stash.
        /// </summary>
        public void UnstashAll()
        {
            UnstashAll(envelope => true);
        }

        /// <summary>
        /// Unstash all of the <see cref="Envelope"/>s in the Stash.
        /// </summary>
        /// <param name="predicate">A predicate function to determine which messages to select.</param>
        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            if(_theStash.Count > 0)
            {
                try
                {
                    foreach(var item in _theStash.Reverse().Where(predicate))
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
     
        /// <summary>
        /// Eliminates the contents of the <see cref="IStash"/>, and returns
        /// the previous contents of the messages.
        /// </summary>
        /// <returns>Previously stashed messages.</returns>
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


