//-----------------------------------------------------------------------
// <copyright file="ActorRef.Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    ///     An extension method class for working with ActorRefs
    /// </summary>
    public static class ActorRefExtensions
    {
        /// <summary>
        ///     If we call a method such as <code>Context.Child(name)</code>
        ///     and don't receive a valid result in return, this method will indicate
        ///     whether or not the actor we received is valid.
        /// </summary>
        public static bool IsNobody(this IActorRef actorRef)
        {
            return actorRef == null || actorRef is Nobody || actorRef is NoSender || actorRef is DeadLetterActorRef;
        }
    }
}
