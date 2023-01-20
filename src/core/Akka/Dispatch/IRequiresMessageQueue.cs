//-----------------------------------------------------------------------
// <copyright file="IRequiresMessageQueue.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Dispatch
{
    /// <summary>
    /// Interface to signal that an Actor requires a certain type of message queue semantics.
    /// <para>
    /// The mailbox type will be looked up by mapping the type T via <c>akka.actor.mailbox.requirements</c> in the config,
    /// to a mailbox configuration. If no mailbox is assigned on Props or in deployment config then this one will be used.
    /// </para>
    /// <para>
    /// The queue type of the created mailbox will be checked against the type T and actor creation will fail if it doesn't
    /// fulfill the requirements.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of <see cref="ISemantics"/> required</typeparam>
    public interface IRequiresMessageQueue<T> 
        where T : ISemantics
    {
    }
}

