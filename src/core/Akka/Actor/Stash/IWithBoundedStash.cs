//-----------------------------------------------------------------------
// <copyright file="IWithBoundedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// Lets the <see cref="StashFactory"/> know that this Actor needs stash support
    /// with restricted storage capacity
    /// You need to add the property:
    /// <code>public IStash Stash { get; set; }</code>
    /// </summary>
    // ReSharper disable once InconsistentNaming
    [Obsolete("Use `IWithStash` with a configured BoundedDeque-based mailbox instead.")]
    public interface IWithBoundedStash : IWithUnrestrictedStash, IRequiresMessageQueue<IBoundedDequeBasedMessageQueueSemantics>
    { }
}

