//-----------------------------------------------------------------------
// <copyright file="ActorRef.Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    }
}

