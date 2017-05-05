//-----------------------------------------------------------------------
// <copyright file="IActorRefFactory.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;

namespace Akka.Actor
{
    /// <summary>
    /// Interface IActorRefFactory
    /// </summary>
    // ReSharper disable once InconsistentNaming
    public interface IActorRefFactory
    {
        /// <summary>
        /// Create new actor as child of this context with the given name, which must
        /// not start with "$". If the given name is already in use,
        /// and <see cref="InvalidActorNameException"/> is thrown.
        /// 
        /// See <see cref="Props"/> for details on how to obtain a <see cref="Props"/> object.
        /// </summary>
        /// <param name="props">The props used to create this actor.</param>
        /// <param name="name">Optional. The name of this actor.</param>
        /// <returns>A reference to the underlying actor.</returns>
        /// <exception cref="InvalidActorNameException">Thrown if the given name is
        /// invalid or already in use</exception>
        /// <exception cref="ConfigurationException">Thrown if deployment, dispatcher
        /// or mailbox configuration is wrong</exception>
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

