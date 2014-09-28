using System;

namespace Akka.Actor
{
    /// <summary>
    /// Public interface used to expose stash capabilites to user-level actors
    /// </summary>
    public interface IStash
    {
        /// <summary>
        /// Stashes the current message
        /// </summary>
        void Stash();

        /// <summary>
        /// Unstash the oldest message in the stash
        /// </summary>
        void Unstash();

        /// <summary>
        /// Unstashes all messages
        /// </summary>
        void UnstashAll();

        /// <summary>
        /// Unstashes all messages selected by the predicate function
        /// </summary>
        void UnstashAll(Func<Envelope, bool> predicate);
    }
}