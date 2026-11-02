//-----------------------------------------------------------------------
// <copyright file="StashFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor.Internal;
using Akka.Util;

namespace Akka.Actor
{
    /// <summary>
    /// Static factor used for creating Stash instances
    /// </summary>
    public static class StashFactory
    {
        /// <summary>
        /// Creates a new <see cref="IStash"/> for the actor type <typeparamref name="T"/> using the given <paramref name="context"/>.
        /// The stash type (bounded, unbounded, or unrestricted) is determined by which <see cref="IActorStash"/>
        /// sub-interface <typeparamref name="T"/> implements.
        /// </summary>
        /// <typeparam name="T">The actor type. Must implement a sub-interface of <see cref="IActorStash"/>.</typeparam>
        /// <param name="context">The actor context used to configure the stash and its underlying mailbox.</param>
        /// <returns>A new <see cref="IStash"/> instance appropriate for the actor type.</returns>
        public static IStash CreateStash<T>(this IActorContext context) where T : ActorBase
        {
            var actorType = typeof(T);
            return CreateStash(context, actorType);
        }

        /// <summary>
        /// Creates a new <see cref="IStash"/> for the given <paramref name="actorInstance"/>.
        /// The stash type is determined by which <see cref="IActorStash"/> sub-interface the actor implements.
        /// </summary>
        /// <param name="context">The actor context used to configure the stash and its underlying mailbox.</param>
        /// <param name="actorInstance">The actor instance whose runtime type determines the stash variant.</param>
        /// <returns>A new <see cref="IStash"/> instance appropriate for the actor type.</returns>
        public static IStash CreateStash(this IActorContext context, IActorStash actorInstance) =>
            CreateStash(context, actorInstance.GetType());

        /// <summary>
        /// Creates a new <see cref="IStash"/> for the given <paramref name="actorType"/>.
        /// Returns a <see cref="BoundedStashImpl"/> for <see cref="IWithBoundedStash"/>,
        /// an <see cref="UnboundedStashImpl"/> for <see cref="IWithUnboundedStash"/>,
        /// or an unrestricted stash for <see cref="IWithUnrestrictedStash"/>.
        /// </summary>
        /// <param name="context">The actor context used to configure the stash and its underlying mailbox.</param>
        /// <param name="actorType">The actor <see cref="Type"/>. Must implement a sub-interface of <see cref="IActorStash"/>.</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="actorType"/> implements an unrecognized subclass of <see cref="IActorStash"/>.
        /// </exception>
        /// <returns>A new <see cref="IStash"/> instance appropriate for the actor type.</returns>
        public static IStash CreateStash(this IActorContext context, Type actorType)
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (actorType.Implements<IWithBoundedStash>())
                return new BoundedStashImpl(context);
#pragma warning restore CS0618 // Type or member is obsolete

            if (actorType.Implements<IWithUnboundedStash>())
                return new UnboundedStashImpl(context);

            if (actorType.Implements<IWithUnrestrictedStash>())
                return new UnrestrictedStashImpl(context);

            throw new ArgumentException($"Actor {actorType} implements an unrecognized subclass of {typeof(IActorStash)} - cannot instantiate", nameof(actorType));
        }
    }
}
