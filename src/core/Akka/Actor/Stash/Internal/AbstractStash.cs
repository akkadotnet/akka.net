//-----------------------------------------------------------------------
// <copyright file="AbstractStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Akka.Dispatch;
using Akka.Dispatch.MessageQueues;
using Akka.Util;
using Akka.Util.Internal;

namespace Akka.Actor.Internal
{
    /// <summary>
    /// INTERNAL API
    /// <para>
    /// Support class for implementing a stash for an actor instance. A default stash per actor (= user stash)
    /// is maintained by this class. Actors that explicitly need other stashes
    /// (optionally in addition to and isolated from the user stash) can create new stashes via <see cref="StashFactory"/>.
    /// </para>
    /// </summary>
    public abstract class AbstractStash : IStash
    {
        /// <summary>
        /// The private stash of the actor. It is only accessible using <see cref="Stash()"/> and
        /// <see cref="UnstashAll()"/>.
        /// </summary>
        private LinkedList<Envelope> _theStash = new();

        private readonly ActorCell _actorCell;

        /// <summary>
        /// The actor's deque-based message queue. 
        /// `mailbox.queue` is the underlying `Deque`.
        /// </summary>
        private readonly IDequeBasedMessageQueueSemantics _mailbox;

        protected AbstractStash(IActorContext context)
        {
            var actorCell = (ActorCell)context;
            _mailbox = actorCell.Mailbox.MessageQueue as IDequeBasedMessageQueueSemantics;
            if (_mailbox == null)
            {
                var message = $"DequeBasedMailbox required, got: {actorCell.Mailbox.GetType().Name}\n" +
                    "An (unbounded) deque-based mailbox can be configured as follows:\n" +
                    "my-custom-mailbox {\n" +
                    "    mailbox-type = \"Akka.Dispatch.UnboundedDequeBasedMailbox\"\n" +
                    "}\n";

                throw new ActorInitializationException(actorCell.Self, message);
            }
            _actorCell = actorCell;

            // The capacity of the stash. Configured in the actor's deployment. If not there, then the mailbox or dispatcher config.
            Capacity = actorCell.Props.Deploy.StashSize == -1
                ? Capacity = context.System.Mailboxes.StashCapacity(context.Props.Dispatcher, context.Props.Mailbox)
                : Capacity = actorCell.Props.Deploy.StashSize;
        }

        private int _currentEnvelopeId;

        /// <summary>
        /// Adds the current message (the message that the actor received last) to the actor's stash.
        /// </summary>
        /// <exception cref="IllegalActorStateException">This exception is thrown if we attempt to stash the same message more than once.</exception>
        /// <exception cref="StashOverflowException">
        /// This exception is thrown in the event that we're using a <see cref="BoundedMessageQueue"/> for the <see cref="IStash"/> and we've exceeded capacity.
        /// </exception>
        public void Stash()
        {
            var currMsg = _actorCell.CurrentMessage;
            var sender = _actorCell.Sender;

            if (_actorCell.CurrentEnvelopeId == _currentEnvelopeId)
            {
                throw new IllegalActorStateException($"Can't stash the same message {currMsg} more than once");
            }
            _currentEnvelopeId = _actorCell.CurrentEnvelopeId;

            if (Capacity <= 0 || _theStash.Count < Capacity)
                _theStash.AddLast(new Envelope(currMsg, sender));
            else 
                throw new StashOverflowException($"Couldn't enqueue message {currMsg} from ${sender} to stash of {_actorCell.Self}");
        }

        /// <summary>
        /// Prepends the oldest message in the stash to the mailbox, and then removes that
        /// message from the stash.
        /// <para>
        /// Messages from the stash are enqueued to the mailbox until the capacity of the
        /// mailbox (if any) has been reached. In case a bounded mailbox overflows, a
        /// `MessageQueueAppendFailedException` is thrown. 
        /// </para>
        /// The unstashed message is guaranteed to be removed from the stash regardless
        /// if the <see cref="Unstash()"/> call successfully returns or throws an exception.
        /// </summary>
        public void Unstash()
        {
            if (_theStash.Count <= 0)
                return;

            try
            {
                EnqueueFirst(_theStash.Head());
            }
            finally
            {
                _theStash.RemoveFirst();
            }
        }

        /// <summary>
        /// Prepends all messages in the stash to the mailbox, and then clears the stash.
        /// <para>
        /// Messages from the stash are enqueued to the mailbox until the capacity of the
        /// mailbox(if any) has been reached. In case a bounded mailbox overflows, a
        /// `MessageQueueAppendFailedException` is thrown. 
        /// </para>
        /// The stash is guaranteed to be empty after calling <see cref="UnstashAll()"/>.
        /// </summary>
        public void UnstashAll() => UnstashAll(_ => true);

        /// <summary>
        /// INTERNA API
        /// <para>
        /// Prepends selected messages in the stash, applying `filterPredicate`,  to the
        /// mailbox, and then clears the stash.
        /// </para>
        /// <para>
        /// Messages from the stash are enqueued to the mailbox until the capacity of the
        /// mailbox(if any) has been reached. In case a bounded mailbox overflows, a
        /// `MessageQueueAppendFailedException` is thrown.
        /// </para>
        /// The stash is guaranteed to be empty after calling <see cref="UnstashAll(Func{Envelope, bool})"/>.
        /// </summary>
        /// <param name="filterPredicate">Only stashed messages selected by this predicate are prepended to the mailbox.</param>
        public void UnstashAll(Func<Envelope, bool> filterPredicate)
        {
            if (_theStash.Count <= 0)
                return;

            try
            {
                foreach (var item in _theStash.Reverse().Where(filterPredicate))
                    EnqueueFirst(item);
            }
            finally
            {
                _theStash = new LinkedList<Envelope>();
            }
        }

        /// <summary>
        /// INTERNAL API.
        /// <para>
        /// Clears the stash and and returns all envelopes that have not been unstashed.
        /// </para>
        /// </summary>
        /// <returns>Previously stashed messages.</returns>
        public IEnumerable<Envelope> ClearStash()
        {
            if (_theStash.Count == 0)
                return Enumerable.Empty<Envelope>();

            var stashed = _theStash;
            _theStash = new LinkedList<Envelope>();
            return stashed;
        }

        /// <summary>
        /// Prepends `others` to this stash. This method is optimized for a large stash and
        /// small `others`.
        /// </summary>
        /// <param name="envelopes">TBD</param>
        public void Prepend(IEnumerable<Envelope> envelopes)
        {
            // since we want to save the order of messages, but still prepending using AddFirst,
            // we must enumerate envelopes in reversed order
            foreach (var envelope in envelopes.Distinct().Reverse())
            {
                _theStash.AddFirst(envelope);
            }
        }

        public int Count => _theStash.Count;
        public bool IsEmpty => Count == 0;
        public bool NonEmpty => !IsEmpty;
        public bool IsFull => Capacity >= 0 && _theStash.Count >= Capacity;

        /// <summary>
        /// The capacity of the stash. Configured in the actor's mailbox or dispatcher config.
        /// </summary>
        /// <remarks>
        /// If capacity is negative, then we're using an Unbounded stash.
        /// </remarks>
        public int Capacity { get; }

        /// <summary>
        /// Enqueues <paramref name="msg"/> at the first position in the mailbox. If the message contained in
        /// the envelope is a <see cref="Terminated"/> message, it will be ensured that it can be re-received
        /// by the actor.
        /// </summary>
        private void EnqueueFirst(Envelope msg)
        {
            _mailbox.EnqueueFirst(msg);
            if (msg.Message is Terminated terminatedMessage)
            {
                _actorCell.TerminatedQueuedFor(terminatedMessage.ActorRef, Option<object>.None);
            }
        }
    }
}


