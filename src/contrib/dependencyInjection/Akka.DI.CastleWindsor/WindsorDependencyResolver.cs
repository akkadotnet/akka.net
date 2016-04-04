//-----------------------------------------------------------------------
// <copyright file="WindsorDependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using Akka.Actor;
using Akka.DI.Core;
using Castle.Windsor;

namespace Akka.DI.CastleWindsor
{
    /// <summary>
    /// Provides services to the <see cref="ActorSystem "/> extension system
    /// used to create actors using the CastleWindsor IoC container.
    /// </summary>
    public class WindsorDependencyResolver : IDependencyResolver
    {
        private IWindsorContainer container;
        private ConcurrentDictionary<string, Type> typeCache;
        private ActorSystem system;

        /// <summary>
        /// Initializes a new instance of the <see cref="WindsorDependencyResolver"/> class.
        /// </summary>
        /// <param name="container">The container used to resolve references</param>
        /// <param name="system">The actor system to plug into</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="container"/> or the <paramref name="system"/> was null.
        /// </exception>
        public WindsorDependencyResolver(IWindsorContainer container, ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            this.system = system;
            this.system.AddDependencyResolver(this);
        }

        /// <summary>
        /// Retrieves an actor's type with the specified name
        /// </summary>
        /// <param name="actorName">The name of the actor to retrieve</param>
        /// <returns>The type with the specified actor name</returns>
        public Type GetType(string actorName)
        {
            typeCache.
                TryAdd(actorName,
                        actorName.GetTypeValue() ??
                        container.
                        Kernel.
                        GetAssignableHandlers(typeof(object)).
                        Where(handler => handler.ComponentModel.Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase)).
                        Select(handler => handler.ComponentModel.Implementation).
                        FirstOrDefault());

            return typeCache[actorName];
        }

        /// <summary>
        /// Creates a delegate factory used to create actors based on their type
        /// </summary>
        /// <param name="actorType">The type of actor that the factory builds</param>
        /// <returns>A delegate factory used to create actors</returns>
        public Func<ActorBase> CreateActorFactory(Type actorType)
        {
            return () => (ActorBase)container.Resolve(actorType);
        }

        /// <summary>
        /// Used to register the configuration for an actor of the specified type <typeparamref name="TActor"/>
        /// </summary>
        /// <typeparam name="TActor">The type of actor the configuration is based</typeparam>
        /// <returns>The configuration object for the given actor type</returns>
        public Props Create<TActor>() where TActor : ActorBase
        {
            return Create(typeof(TActor));
        }

        /// <summary>
        /// Used to register the configuration for an actor of the specified type <paramref name="actorType"/> 
        /// </summary>
        /// <param name="actorType">The <see cref="Type"/> of actor the configuration is based</param>
        /// <returns>The configuration object for the given actor type</returns>
        public virtual Props Create(Type actorType)
        {
            return system.GetExtension<DIExt>().Props(actorType);
        }

        /// <summary>
        /// Signals the container to release it's reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to remove from the container</param>
        public void Release(ActorBase actor)
        {
            this.container.Release(actor);
        }
    }
}
