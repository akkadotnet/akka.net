//-----------------------------------------------------------------------
// <copyright file="IActorRefFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Actor
{
    /// <summary>
    /// Interface IActorRefFactory
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface IActorRefFactory
    {
        //TODO: Fix comment
        /// <summary>
        /// Create new actor as child of this context with the given name, which must
        /// not start with “$”. If the given name is already in use,
        /// and `InvalidActorNameException` is thrown.
        /// See <see cref="Props"/> for details on how to obtain a <see cref="Props"/> object.
        /// @throws akka.actor.InvalidActorNameException if the given name is
        /// invalid or already in use
        /// @throws akka.ConfigurationException if deployment, dispatcher
        /// or mailbox configuration is wrong
        /// </summary>
        /// <param name="props">The props.</param>
        /// <param name="name">The name.</param>
        /// <returns>InternalActorRef.</returns>
        IActorRef ActorOf(Props props, string name = null);

       
        /// <summary>
        /// Construct an <see cref="Akka.Actor.ActorSelection" /> from the given path, which is
        /// parsed for wildcards (these are replaced by regular expressions
        /// internally). No attempt is made to verify the existence of any part of
        /// the supplied path, it is recommended to send a message and gather the
        /// replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        ActorSelection ActorSelection(ActorPath actorPath);

        /// <summary>
        /// Construct an <see cref="Akka.Actor.ActorSelection" /> from the given path, which is
        /// parsed for wildcards (these are replaced by regular expressions
        /// internally). No attempt is made to verify the existence of any part of
        /// the supplied path, it is recommended to send a message and gather the
        /// replies in order to resolve the matching set of actors.
        /// </summary>
        /// <param name="actorPath">The actor path.</param>
        /// <returns>ActorSelection.</returns>
        ActorSelection ActorSelection(string actorPath);
    }
}

