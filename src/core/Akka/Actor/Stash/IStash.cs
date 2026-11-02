//-----------------------------------------------------------------------
// <copyright file="IStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Akka.Actor
{
    /// <summary>
    /// Public interface used to expose stash capabilities to user-level actors
    /// </summary>
    public interface IStash
    {
        /// <summary>
        /// Stashes the current message (the message that the actor received last)
        /// </summary>
        void Stash();

        /// <summary>
        /// Unstash the oldest message in the stash and prepends it to the actor's mailbox.
        /// The message is removed from the stash.
        /// </summary>
        void Unstash();

        /// <summary>
        /// Unstashes all messages by prepending them to the actor's mailbox.
        /// The stash is guaranteed to be empty afterwards.
        /// </summary>
        void UnstashAll();

        /// <summary>
        /// Unstashes all messages selected by the predicate function. Other messages are discarded.
        /// The stash is guaranteed to be empty afterwards.
        /// </summary>
        /// <param name="predicate">A filter applied to each stashed <see cref="Envelope"/>. Only messages for which this returns <see langword="true"/> are prepended to the mailbox; the rest are discarded.</param>
        void UnstashAll(Func<Envelope, bool> predicate);

        /// <summary>
        /// Returns all messages and clears the stash.
        /// The stash is guaranteed to be empty afterwards.
        /// </summary>
        /// <returns>The previous stashed messages.</returns>
        IEnumerable<Envelope> ClearStash();

        /// <summary>
        /// Prepend a set of envelopes to the front of the stash.
        /// </summary>
        /// <param name="envelopes">The collection of <see cref="Envelope"/> messages to insert at the front of the stash.</param>
        void Prepend(IEnumerable<Envelope> envelopes);
        
        /// <summary>
        /// The number of messages currently inside the stash.
        /// </summary>
        public int Count { get; }
        
        /// <summary>
        /// Returns <see langword="true"/> when <see cref="Count"/> is zero.
        /// </summary>
        public bool IsEmpty { get; }
        
        /// <summary>
        /// Returns <see langword="true"/> when <see cref="Count"/> is greater than zero.
        /// </summary>
        public bool NonEmpty { get; }
        
        /// <summary>
        /// When using a bounded stash, this returns <see langword="true"/> when the stash is full.
        /// </summary>
        /// <remarks>
        /// Always returns <see langword="false"/> when using an unbounded stash.
        /// </remarks>
        public bool IsFull { get; }
        
        /// <summary>
        /// The total capacity of the stash.
        /// </summary>
        public int Capacity { get; }
    }
}

