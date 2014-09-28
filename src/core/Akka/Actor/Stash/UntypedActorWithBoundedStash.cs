using System;
using Akka.Actor.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// An UntypedActor with bounded Stash capabilites
    /// </summary>
    public abstract class UntypedActorWithBoundedStash : UntypedActor, WithBoundedStash
    {

        private IStash _stash = new BoundedStashImpl(Context);

        /// <summary>
        /// The stash implementation available for this actor
        /// </summary>
        public IStash CurrentStash { get { return _stash; } set { _stash = value; } }

        /// <summary>
        /// Stashes the current message
        /// </summary>
        public void Stash()
        {
            CurrentStash.Stash();
        }

        /// <summary>
        /// Unstash the oldest message in the stash
        /// </summary>
        public void Unstash()
        {
            CurrentStash.Unstash();
        }

        /// <summary>
        /// Unstashes all messages
        /// </summary>
        public void UnstashAll()
        {
            CurrentStash.UnstashAll();
        }

        /// <summary>
        /// Unstashes all messages selected by the predicate function
        /// </summary>
        public void UnstashAll(Func<Envelope, bool> predicate)
        {
            CurrentStash.UnstashAll(predicate);
        }

        #region ActorBase overrides

        /// <summary>
        /// Overridden callback. Prepends all messages in the stash to the mailbox,
        /// clears the stash, stops all children, and invokes the PostStop callback.
        /// </summary>
        protected override void PreRestart(Exception reason, object message)
        {
            try
            {
                CurrentStash.UnstashAll();
            }
            finally
            {
                base.PreRestart(reason, message);
            }

        }

        /// <summary>
        /// Overridden callback. Prepends all messages in the stash to the mailbox,
        /// clears the stash. Must be called when overriding this method; otherwise stashed messages won't be
        /// propagated to DeadLetters when actor stops.
        /// </summary>
        protected override void PostStop()
        {
            try
            {
                CurrentStash.UnstashAll();
            }
            finally
            {
                base.PostStop();
            }

        }

        #endregion
    }
}
