//-----------------------------------------------------------------------
// <copyright file="StashFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="context">TBD</param>
        /// <returns>TBD</returns>
        public static IStash CreateStash<T>(this IActorContext context) where T:ActorBase
        {
            var actorType = typeof(T);
            return CreateStash(context, actorType);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="actorInstance">TBD</param>
        /// <returns>TBD</returns>
        public static IStash CreateStash(this IActorContext context, IActorStash actorInstance)
        {
            return CreateStash(context, actorInstance.GetType());
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="context">TBD</param>
        /// <param name="actorType">TBD</param>
        /// <exception cref="ArgumentException">
        /// This exception is thrown if the given <paramref name="actorType"/> implements an unrecognized subclass of <see cref="IActorStash"/>.
        /// </exception>
        /// <returns>TBD</returns>
        public static IStash CreateStash(this IActorContext context, Type actorType)
        {
            if(actorType.Implements<IWithBoundedStash>())
            {
                return new BoundedStashImpl(context);
            }

            if(actorType.Implements<IWithUnboundedStash>())
            {
                return new UnboundedStashImpl(context);
            }

            throw new ArgumentException($"Actor {actorType} implements an unrecognized subclass of {typeof(IActorStash)} - cannot instantiate", nameof(actorType));
        }
    }
}
