//-----------------------------------------------------------------------
// <copyright file="UntypedActorWithStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Actor base class that should be extended to create an actor with a stash.
    /// <para>
    /// The stash enables an actor to temporarily stash away messages that can not or
    /// should not be handled using the actor's current behavior.
    /// </para>
    /// <para>
    /// Note that the subclasses of `UntypedActorWithStash` by default request a Deque based mailbox since this class
    /// implements the <see cref="IRequiresMessageQueue{T}"/> marker interface.
    /// </para>
    /// You can override the default mailbox provided when `IDequeBasedMessageQueueSemantics` are requested via config:
    /// <code>
    /// akka.actor.mailbox.requirements {
    ///     "Akka.Dispatch.IDequeBasedMessageQueueSemantics" = your-custom-mailbox
    /// }
    /// </code>
    /// Alternatively, you can add your own requirement marker to the actor and configure a mailbox type to be used
    /// for your marker.
    /// <para>
    /// For a `Stash` based actor that enforces unbounded  deques see <see cref="UntypedActorWithUnboundedStash"/>.
    /// There is also an unrestricted version <see cref="UntypedActorWithUnrestrictedStash"/> that does not
    /// enforce the mailbox type.
    /// </para>
    /// </summary>
    public abstract class UntypedActorWithStash : UntypedActor, IWithStash
    {
        public IStash Stash { get; set; }
    }

    /// <summary>
    /// Actor base class with `Stash` that enforces an unbounded deque for the actor.
    /// See <see cref="UntypedActorWithStash"/> for details on how `Stash` works.
    /// </summary>
    public abstract class UntypedActorWithUnboundedStash : UntypedActor, IWithUnboundedStash
    {
        public IStash Stash { get; set; }
    }

    /// <summary>
    /// Actor base class with `Stash` that does not enforce any mailbox type. The proper mailbox has to be configured
    /// manually, and the mailbox should extend the <see cref="IDequeBasedMessageQueueSemantics"/> marker interface.
    /// See <see cref="UntypedActorWithStash"/> for details on how `Stash` works.
    /// </summary>
    public abstract class UntypedActorWithUnrestrictedStash : UntypedActor, IWithUnrestrictedStash
    {
        public IStash Stash { get; set; }
    }
}
