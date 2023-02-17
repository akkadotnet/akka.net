//-----------------------------------------------------------------------
// <copyright file="IWithUnboundedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// The `IWithUnboundedStash` interface is a version of <see cref="IActorStash"/> that enforces an unbounded stash for you actor.
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface IWithUnboundedStash : IWithUnrestrictedStash, IRequiresMessageQueue<IUnboundedDequeBasedMessageQueueSemantics>
    {
    }
}

