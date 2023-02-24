//-----------------------------------------------------------------------
// <copyright file="IWithUnboundedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// The `IWithStash` interface enables an actor to temporarily stash away messages that can not or
    /// should not be handled using the actor's current behavior. 
    /// <para>
    /// Note that the `IWithStash` interface can only be used together with actors that have a deque-based
    /// mailbox. By default Stash based actors request a Deque based mailbox since the stash
    /// interface extends <see cref="IRequiresMessageQueue{T}"/>.
    /// </para>
    /// You can override the default mailbox provided when `IDequeBasedMessageQueueSemantics` are requested via config:
    /// <code>
    /// akka.actor.mailbox.requirements {
    ///     "Akka.Dispatch.IBoundedDequeBasedMessageQueueSemantics" = your-custom-mailbox
    /// }
    /// </code>
    /// Alternatively, you can add your own requirement marker to the actor and configure a mailbox type to be used
    /// for your marker.
    /// <para>
    /// For a `Stash` that also enforces unboundedness of the deque see <see cref="IWithUnboundedStash"/>. For a `Stash`
    /// that does not enforce any mailbox type see <see cref="IWithUnrestrictedStash"/>.
    /// </para>
    /// </summary>
    public interface IWithStash : IWithUnrestrictedStash, IRequiresMessageQueue<IBoundedDequeBasedMessageQueueSemantics>
    {
    }
}

