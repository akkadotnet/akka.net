//-----------------------------------------------------------------------
// <copyright file="StashFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
        public static IStash CreateStash<T>(this IActorContext context) where T:ActorBase
        {
            var actorType = typeof(T);
            return CreateStash(context, actorType);
        }

        public static IStash CreateStash(this IActorContext context, IActorStash actorInstance)
        {
            return CreateStash(context, actorInstance.GetType());
        }

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

            throw new ArgumentException(string.Format("Actor {0} implements unrecognized subclass of {1} - cannot instantiate",
                actorType, typeof(IActorStash)));
        }
    }
}
