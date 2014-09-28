using System;
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
            if(actorType.Implements<WithBoundedStash>())
            {
                return new BoundedStashImpl(context);
            }

            if(actorType.Implements<WithUnboundedStash>())
            {
                return new UnboundedStashImpl(context);
            }

            throw new ArgumentException(string.Format("Actor {0} implements unrecognized subclass of {1} - cannot instantiate",
                actorType, typeof(IActorStash)));
        }
    }
}