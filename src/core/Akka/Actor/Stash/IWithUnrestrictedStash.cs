//-----------------------------------------------------------------------
// <copyright file="IWithUnrestrictedStash.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Dispatch;

namespace Akka.Actor
{
    /// <summary>
    /// A version of <see cref="IActorStash"/> that does not enforce any mailbox type. The proper mailbox has to be configured
    /// manually, and the mailbox should extend the <see cref="IDequeBasedMessageQueueSemantics"/> marker interface.
    /// </summary>
    public interface IWithUnrestrictedStash : IActorStash
    { 
    }
}
