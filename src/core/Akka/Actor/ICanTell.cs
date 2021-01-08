//-----------------------------------------------------------------------
// <copyright file="ICanTell.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// A shared interface for both <see cref="IActorRef"/> and <see cref="ActorSelection"/>,
    /// both of which can be sent messages via the <see cref="Tell"/> command.
    /// </summary>
    public interface ICanTell
    {
        /// <summary>
        /// Asynchronously delivers a message to this <see cref="IActorRef"/> or <see cref="ActorSelection"/>
        /// in a non-blocking fashion. Uses "at most once" delivery semantics.
        /// </summary>
        /// <param name="message">The message to be sent to the target.</param>
        /// <param name="sender">The sender of this message. Defaults to <see cref="ActorRefs.NoSender"/> if left to <c>null</c>.</param>
        void Tell(object message, IActorRef sender);
    }
}

