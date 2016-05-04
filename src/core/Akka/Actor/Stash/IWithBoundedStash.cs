//-----------------------------------------------------------------------
// <copyright file="IWithBoundedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    [Obsolete("Bounded stashing is not yet implemented. Unbounded stashing will be used instead")]
    public interface IWithBoundedStash : IActorStash, IRequiresMessageQueue<IBoundedDequeBasedMessageQueueSemantics>
    { }
}

