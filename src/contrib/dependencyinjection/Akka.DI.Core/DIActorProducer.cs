﻿//-----------------------------------------------------------------------
// <copyright file="DIActorProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class represents an actor creation strategy that uses dependency injection (DI) to resolve and instantiate actors based on their type.
    /// </summary>
    public class DIActorProducer : IIndirectActorProducer
    {
        private IDependencyResolver dependencyResolver;
        private Type actorType;

        readonly Func<ActorBase> actorFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="DIActorProducer"/> class.
        /// </summary>
        /// <param name="dependencyResolver">The resolver used to resolve the given actor type.</param>
        /// <param name="actorType">The type of actor that this producer creates.</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="dependencyResolver"/> or the <paramref name="actorType"/> was null.
        /// </exception>
        public DIActorProducer(IDependencyResolver dependencyResolver, Type actorType)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            if (actorType == null) throw new ArgumentNullException("actorType");

            this.dependencyResolver = dependencyResolver;
            this.actorType = actorType;
            this.actorFactory = dependencyResolver.CreateActorFactory(actorType);
        }

        /// <summary>
        /// Retrieves the type of the actor to produce.
        /// </summary>
        public Type ActorType
        {
            get { return this.actorType; }
        }

        /// <summary>
        /// Creates an actor based on the container's implementation specific actor factory.
        /// </summary>
        /// <returns>An actor created by the container.</returns>
        public ActorBase Produce()
        {
            return actorFactory();
        }

        /// <summary>
        /// Signals the container that it can release its reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to remove from the container.</param>
        public void Release(ActorBase actor)
        {
            dependencyResolver.Release(actor);
        }
    }
}
