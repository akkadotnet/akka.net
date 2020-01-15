//-----------------------------------------------------------------------
// <copyright file="ActorRefFactoryShared.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor.Internal;

namespace Akka.Actor
{
    /// <summary>
    /// This class contains implementations originally found in Akka´s trait ActorRefFactory in ActorRefProvider.scala
    /// https://github.com/akka/akka/blob/master/akka-actor/src/main/scala/akka/actor/ActorRefProvider.scala#L180
    /// <see cref="IActorRefFactory"/> corresponds to that trait, but since it is an interface it
    /// cannot contain any code, hence this class.
    /// </summary>
    public static class ActorRefFactoryShared
    {
        /// <summary>
        ///     Construct an <see cref="Akka.Actor.ActorSelection"/> from the given path, which is
        ///     parsed for wildcards (these are replaced by regular expressions
        ///     internally). No attempt is made to verify the existence of any part of
        ///     the supplied path, it is recommended to send a message and gather the
        ///     replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">TBD</param>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static ActorSelection ActorSelection(ActorPath actorPath, ActorSystem system)
        {
            return new ActorSelection(((ActorSystemImpl)system).Provider.RootGuardianAt(actorPath.Address), actorPath.Elements);
        }

        /// <summary>
        ///     Construct an <see cref="Akka.Actor.ActorSelection"/> from the given path, which is
        ///     parsed for wildcards (these are replaced by regular expressions
        ///     internally). No attempt is made to verify the existence of any part of
        ///     the supplied path, it is recommended to send a message and gather the
        ///     replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="path">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="lookupRoot">TBD</param>
        /// <returns>TBD</returns>
        public static ActorSelection ActorSelection(string path, ActorSystem system, IActorRef lookupRoot)
        {
            var provider = ((ActorSystemImpl)system).Provider;

            //no path given
            if (string.IsNullOrEmpty(path))
            {
                return new ActorSelection(system.DeadLetters, "");
            }

            //absolute path
            var elements = path.Split('/');
            if (elements[0] == "")
            {
                return new ActorSelection(provider.RootGuardian, elements.Skip(1));
            }

            if(Uri.IsWellFormedUriString(path, UriKind.Absolute))
            {
                ActorPath actorPath;
                if(!ActorPath.TryParse(path, out actorPath))
                    return new ActorSelection(provider.DeadLetters, "");

                var actorRef = provider.RootGuardianAt(actorPath.Address);
                return new ActorSelection(actorRef, actorPath.Elements);
            }
            
            return new ActorSelection(lookupRoot, path);
        }

        /// <summary>
        ///     Construct an <see cref="Akka.Actor.ActorSelection"/> from the given string representing a path
        ///     relative to the given target. This operation has to create all the
        ///     matching magic, so it is preferable to cache its result if the
        ///     intention is to send messages frequently.
        /// </summary>
        /// <param name="anchorActorRef">TBD</param>
        /// <param name="path">TBD</param>
        /// <returns>TBD</returns>
        public static ActorSelection ActorSelection(IActorRef anchorActorRef, string path)
        {
            return new ActorSelection(anchorActorRef, path);
        }
    }
}

