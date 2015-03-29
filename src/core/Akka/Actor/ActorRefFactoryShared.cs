﻿using System;
using System.Linq;
using Akka.Actor.Internals;

namespace Akka.Actor
{
    /// <summary>
    /// This class contains implementations originally found in Akka´s trait ActorRefFactory in ActorRefProvider.scala
    /// https://github.com/akka/akka/blob/master/akka-actor/src/main/scala/akka/actor/ActorRefProvider.scala#L180
    /// <see cref="ActorRefFactory"/> corresponds to that trait, but since it is an interface it
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
        public static ActorSelection ActorSelection(string path, ActorSystem system, ActorRef lookupRoot)
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
    }
}