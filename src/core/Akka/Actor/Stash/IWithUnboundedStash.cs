//-----------------------------------------------------------------------
// <copyright file="IWithUnboundedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with unrestricted storage capacity.
    /// You need to add the property:
    /// <code>public IStash Stash { get; set; }</code>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface IWithUnboundedStash : IActorStash, IRequiresMessageQueue<IUnboundedDequeBasedMessageQueueSemantics>
    {
    }
}

