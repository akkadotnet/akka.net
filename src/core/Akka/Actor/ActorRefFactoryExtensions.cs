//-----------------------------------------------------------------------
// <copyright file="ActorRefFactoryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    public static class ActorRefFactoryExtensions
    {
        public static IActorRef ActorOf<TActor>(this IActorRefFactory factory, string name = null) where TActor : ActorBase, new()
        {
            return factory.ActorOf(Props.Create<TActor>(), name: name);
        }

        /// <summary>
        ///     Construct an <see cref="Akka.Actor.ActorSelection"/> from the given string representing a path
        ///     relative to the given target. This operation has to create all the
        ///     matching magic, so it is preferable to cache its result if the
        ///     intention is to send messages frequently.
        /// </summary>
        public static ActorSelection ActorSelection(this IActorRefFactory factory, IActorRef anchorRef, string actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(anchorRef, actorPath);
        }

    }
}

