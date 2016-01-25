//-----------------------------------------------------------------------
// <copyright file="DIActorProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// Dependency Injection Backed IndirectActorProducer
    /// </summary>
    public class DIActorProducer : IIndirectActorProducer
    {
        private IDependencyResolver dependencyResolver;
        private Type actorType;

        readonly Func<ActorBase> actorFactory;

        public DIActorProducer(IDependencyResolver dependencyResolver,
                               Type actorType)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            if (actorType == null) throw new ArgumentNullException("actorType");

            this.dependencyResolver = dependencyResolver;
            this.actorType = actorType;
            this.actorFactory = dependencyResolver.CreateActorFactory(actorType);
        }
        /// <summary>
        /// The System.Type of the Actor specified in the constructor parameter actorName
        /// </summary>
        public Type ActorType
        {
            get { return this.actorType; }
        }
        /// <summary>
        /// Creates an instance of the Actor based on the Type specified in the constructor parameter actorName
        /// </summary>
        /// <returns></returns>
        public ActorBase Produce()
        {
            return actorFactory();
        }

        /// <summary>
        /// This method is used to signal the container that it can release it's reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to remove from the container</param>
        public void Release(ActorBase actor)
        {
            dependencyResolver.Release(actor);
        }
    }
}

