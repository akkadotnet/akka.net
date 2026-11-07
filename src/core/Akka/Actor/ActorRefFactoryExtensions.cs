//-----------------------------------------------------------------------
// <copyright file="ActorRefFactoryExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Akka.Actor
{
    /// <summary>
    /// This class contains extension methods used for working with <see cref="IActorRefFactory"/>.
    /// </summary>
    public static class ActorRefFactoryExtensions
    {
        /// <summary>
        /// Creates an actor of type <typeparamref name="TActor"/> through this factory.
        /// </summary>
        /// <typeparam name="TActor">The actor type to create. It must have a public parameterless constructor.</typeparam>
        /// <param name="factory">The actor reference factory that will create the actor.</param>
        /// <param name="name">The actor's local name, or <c>null</c> to let the factory assign one.</param>
        /// <returns>The reference to the newly created actor.</returns>
        public static IActorRef ActorOf<[DynamicallyAccessedMembers(Props.ActorTypeMembers)] TActor>(this IActorRefFactory factory, string name = null)
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
        /// <param name="factory">The actor reference factory used to create the selection.</param>
        /// <param name="anchorRef">The actor reference against which the relative path is resolved.</param>
        /// <param name="actorPath">A path expression relative to <paramref name="anchorRef"/>. Wildcards may match multiple actors.</param>
        /// <returns>A selection that can send messages to actors matching the path.</returns>
        public static ActorSelection ActorSelection(this IActorRefFactory factory, IActorRef anchorRef, string actorPath)
        {
            return ActorRefFactoryShared.ActorSelection(anchorRef, actorPath);
        }
    }
}
