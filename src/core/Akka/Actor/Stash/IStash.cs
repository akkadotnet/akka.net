//-----------------------------------------------------------------------
// <copyright file="IStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// <returns>TBD</returns>
        IEnumerable<Envelope> ClearStash();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="envelopes">TBD</param>
        void Prepend(IEnumerable<Envelope> envelopes);
    }
}

