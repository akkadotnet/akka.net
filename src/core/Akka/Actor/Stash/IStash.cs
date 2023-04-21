//-----------------------------------------------------------------------
// <copyright file="IStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <param name="predicate">TBD</param>
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
        /// <param name="envelopes">TBD</param>
        void Prepend(IEnumerable<Envelope> envelopes);
        
        /// <summary>
        /// The number of messages currently inside the stash.
        /// </summary>
        public int Count { get; }
        
        /// <summary>
        /// Returns <c>true</c> when <see cref="Count"/> is zero.
        /// </summary>
        public bool IsEmpty { get; }
        
        /// <summary>
        /// Returns <c>true</c> when <see cref="Count"/> is greater than zero.
        /// </summary>
        public bool NonEmpty { get; }
        
        /// <summary>
        /// When using a bounded stash, this returns <c>true</c> when the stash is full.
        /// </summary>
        /// <remarks>
        /// Always returns <c>false</c> when using an unbounded stash.
        /// </remarks>
        public bool IsFull { get; }
    }
}

