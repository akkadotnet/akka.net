using System;
using System.Collections.Generic;

namespace Akka.Actor
{
    /// <summary>
    /// INTERNAL API.
    /// 
    /// Support interface for implementing a stash for an actor instance. A default stash per actor (= user stash)
    /// is maintained by <see cref="WithUnboundedStash"/> by extending this interface. Actors that explicitly need other stashes
    /// can create new stashes via <see cref="StashFactory"/>.
    /// </summary>
    internal interface IStashSupport
    {
        /// <summary>
        /// The capacity of the stash.
        /// </summary>
        int Capacity { get; }

        /// <summary>
        /// Adds the current message (the message that the Actor received last) to the actor's stash.
        /// <exception cref="StashOverflowException">In the event of a stash capacity violation</exception>
        /// <exception cref="IllegalActorStateException">If the same message has been stashed more than once</exception>
        /// </summary>
        void StashInternal();

        /// <summary>
        /// Prepend <see cref="others"/> to this stash. This method is optimized for large stash and small <see cref="others"/>.
        /// </summary>
        void PrependInternal(IEnumerable<Envelope> others);

        /// <summary>
        /// Prepends the oldest message in the stash to the mailbox, and the removes that message from the stash.
        /// 
        /// Messages from the stash are enqueued to the mailbox until the capacity of the mailbox (if any) has been
        /// reached. In case a bounded mailbox overflows, an exception is thrown.
        /// 
        /// The unstashed message is guaranteed to be removed from the stash regardless if the unstash call successfully
        /// returns or throws an exception.
        /// </summary>
        void UnstashInternal();

        /// <summary>
        /// Prepends selected messages in the stash, applying <see cref="predicate"/> to the mailbox,
        /// and then clears the stash.
        /// </summary>
        /// <param name="predicate">Only stashed messages selected by this predicate are prepended to the mailbox.</param>
        void UnstashAllInternal(Func<Envelope, bool> predicate);

        /// <summary>
        /// Prepends all messages to the mailbox, without a predicate.
        /// </summary>
        void UnstashAllInternal();

        /// <summary>
        /// Clears the stash and returns all envelopes that have not been stashed.
        /// </summary>
        IEnumerable<Envelope> ClearStash();

        /// <summary>
        /// Enqueue a message to the front of this actor's mailbox
        /// </summary>
        /// <param name="msg"></param>
        void EnqueueFirst(Envelope msg);
    }
}