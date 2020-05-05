//-----------------------------------------------------------------------
// <copyright file="ActorRefFactoryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// This class contains extension methods used for working with <see cref="IActorRefFactory"/>.
    /// </summary>
    public static class ActorRefFactoryExtensions
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="TActor">TBD</typeparam>
        /// <param name="factory">TBD</param>
        /// <param name="name">TBD</param>
        /// <returns>TBD</returns>
        public static IActorRef ActorOf<TActor>(this IActorRefFactory factory, string name = null)
            where TActor : ActorBase, new()
        {
            return factory.ActorOf(Props.Create<TActor>(), name: name);
        }

        /// <summary>
        ///     Construct an <see cref="Akka.Actor.ActorSelection"/> from the given string representing a path
        ///     relative to the given target. This operation has to create all the
        ///     matching magic, so it is preferable to cache its result if the
        ///     intention is to send messages frequently.
        /// </summary>
        /// <param name="factory">TBD</param>
        /// <param name="anchorRef">TBD</param>
        /// <param name="actorPath">TBD</param>
        /// <returns>TBD</returns>
        public static ActorSelection ActorSelection(this IActorRefFactory factory, IActorRef anchorRef, string actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(anchorRef, actorPath);
        }
    }
}
