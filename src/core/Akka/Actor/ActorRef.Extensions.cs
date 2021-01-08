//-----------------------------------------------------------------------
// <copyright file="ActorRef.Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;

namespace Akka.Actor
{
    /// <summary>
    /// This class contains extension methods used for working with <see cref="IActorRef">ActorRefs</see>.
    /// </summary>
    public static class ActorRefExtensions
    {
        /// <summary>
        /// Determines if the specified <paramref name="actorRef"/> is valid.
        /// An <paramref name="actorRef"/> is thought to be invalid if it's one of the following:
        ///    <see langword="null"/>, <see cref="Nobody"/>, and <see cref="DeadLetterActorRef"/>
        /// </summary>
        /// <param name="actorRef">The actor that is being tested.</param>
        /// <returns><c>true</c> if the <paramref name="actorRef"/> is valid; otherwise <c>false</c>.</returns>
        public static bool IsNobody(this IActorRef actorRef)
        {
            return actorRef == null || actorRef is Nobody || actorRef is DeadLetterActorRef;
        }

        /// <summary>
        /// Returns the <paramref name="actorRef"/>'s value if it's not <see langword="null"/>, <see cref="Nobody"/>, 
        /// or <see cref="DeadLetterActorRef"/>. Otherwise return the result of evaluating `elseValue`.
        /// </summary>
        /// <param name="actorRef">The actor that is being tested.</param>
        /// <param name="elseValue">TBD</param>
        public static IActorRef GetOrElse(this IActorRef actorRef, Func<IActorRef> elseValue)
        {
            return actorRef.IsNobody() ? elseValue() : actorRef;
        }
    }
}

