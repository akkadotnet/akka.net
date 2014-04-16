using System;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Actor
{
    /// <summary>
    /// An UntypedActor with bounded Stash capabilites
    /// </summary>
    public abstract class UntypedActorWithBoundedStash : UntypedActor, WithBoundedStash
    {

        private IStash _stash = new BoundedStashImpl(Context);
        public IStash CurrentStash { get; set; }

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

    /// <summary>
    /// An UntypedActor with Unbounded Stash capabilites
    /// </summary>
    public abstract class UntypedActorWithUnboundedStash : UntypedActor, WithUnboundedStash {

        private IStash _stash = new UnboundedStashImpl(Context);
        public IStash CurrentStash { get; set; }

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

    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with unrestricted storage capacity
    /// </summary>
// ReSharper disable once InconsistentNaming
    public interface WithUnboundedStash : IActorStash
    {
        IStash CurrentStash { get; set; }
    }

    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with restricted storage capacity
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface WithBoundedStash : IActorStash { }

    /// <summary>
    /// Marker interface for adding stash support
    /// </summary>
    public interface IActorStash { }

    /// <summary>
    /// Static factor used for creating Stash instances
    /// </summary>
    public static class StashFactory
    {
        public static IStash GetStash(this IActorContext context)
        {
            var actorCell = context.AsInstanceOf<ActorCell>();
            var actor = actorCell.Actor;
            if (!(actor is IActorStash))
            {
                throw new NotSupportedException(string.Format("Cannot create stash for Actor {0} - needs to implement IActorStash interface", actor));
            }

            if (actor is WithBoundedStash)
            {
                return new BoundedStashImpl(context);
            }

            if (actor is WithUnboundedStash)
            {
                return new UnboundedStashImpl(context);
            }

            throw new ArgumentException(string.Format("Actor {0} implements unrecognized subclass of IActorStash - cannot instantiate", actor));
        }
    }

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


    /// <summary>
    /// A stash implementation that is bounded
    /// </summary>
    internal class BoundedStashImpl : AbstractStash
    {
        public BoundedStashImpl(IActorContext context, int capacity = 100)
            : base(context, capacity)
        {
        }
    }

    /// <summary>
    /// A stash implementation that is unbounded
    /// </summary>
    internal class UnboundedStashImpl : AbstractStash
    {
        public UnboundedStashImpl(IActorContext context)
            : base(context, int.MaxValue)
        {
        }
    }

    /// <summary>
    /// Abstract base class for stash support
    /// </summary>
    internal abstract class AbstractStash : IStash, IStashSupport
    {
        protected IActorContext Context;
        protected ActorRef Self;
        protected LinkedList<Envelope> TheStash;
        protected ActorCell ActorCell;

        protected AbstractStash(IActorContext context, int capacity = 100)
        {
            Context = context;
            Self = Context.Self;
            TheStash = new LinkedList<Envelope>();
            ActorCell = context.AsInstanceOf<ActorCell>();
            Capacity = 100;
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
            //TODO: need to add double-ended queue semantics for this to work
            ActorCell.Mailbox.Post(msg);
        }
    }

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

    /// <summary>
    /// Is thrown when the size of the Stash exceeds the capacity of the stash
    /// </summary>
    public class StashOverflowException : AkkaException
    {
        public StashOverflowException(string message, Exception cause = null) : base(message, cause) { }
    }
}
