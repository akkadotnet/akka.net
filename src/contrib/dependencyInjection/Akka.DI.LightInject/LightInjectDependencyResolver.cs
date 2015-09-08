//-----------------------------------------------------------------------
// <copyright file="LightInjectDependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.DI.Core;
using LightInject;
using Scope = LightInject.Scope;

namespace Akka.DI.LightInject
{
    /// <summary>
    /// Provides services to the <see cref="ActorSystem"/> extension system
    /// used to create actors using the LightInject IoC container.
    /// </summary>
    public class LightInjectDependencyResolver : IDependencyResolver
    {
        private readonly IServiceContainer _container;
        private readonly ActorSystem _system;
        private readonly ConcurrentDictionary<string, Type> _typeCache;
        private readonly ConditionalWeakTable<ActorBase, Scope> _references;
        
        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectDependencyResolver"/> class.
        /// </summary>
        /// <param name="container">The container used to resolve references</param>
        /// <param name="system">The actor system to plug into</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="container"/> or the <paramref name="system"/> was null.
        /// </exception>
        public LightInjectDependencyResolver(IServiceContainer container, ActorSystem system)
        {
            if (container == null) throw new ArgumentNullException("container");
            if (system == null) throw new ArgumentNullException("system");

            _container = container;
            _typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            _references = new ConditionalWeakTable<ActorBase, Scope>();

            _system = system;
            _system.AddDependencyResolver(this);
        }

        /// <summary>
        /// Retrieves an actor's type with the specified name
        /// </summary>
        /// <param name="actorName">The name of the actor to retrieve</param>
        /// <returns>The type with the specified actor name</returns>
        public Type GetType(string actorName)
        {
            Type t = actorName.GetTypeValue() ??
                _container.AvailableServices
                    .Where(x => x.ServiceType.Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase))
                    .Select(x => x.ServiceType)
                    .FirstOrDefault();

            _typeCache.TryAdd(actorName, t);

            return _typeCache[actorName];
        }
        
        /// <summary>
        /// Creates a delegate factory used to create actors based on their type
        /// </summary>
        /// <param name="actorType">The type of actor that the factory builds</param>
        /// <returns>A delegate factory used to create actors</returns>
        public Func<ActorBase> CreateActorFactory(Type actorType)
        {
            return () =>
            {
                var scope = _container.BeginScope();
                var actor = (ActorBase)_container.GetInstance(actorType);
                _references.Add(actor, scope);
                return actor;
            };
        }

        /// <summary>
        /// Used to register the configuration for an actor of the specified type <typeparam name="TActor"/>
        /// </summary>
        /// <typeparam name="TActor">The type of actor the configuration is based</typeparam>
        /// <returns>The configuration object for the given actor type</returns>
        public Props Create<TActor>() where TActor : ActorBase
        {
            return _system.GetExtension<DIExt>().Props(typeof(TActor));
        }

        /// <summary>
        /// Signals the DI container to release it's reference to the actor.
        /// <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor">The actor to remove from the container</param>
        public void Release(ActorBase actor)
        {
            Scope scope;

            if (_references.TryGetValue(actor, out scope))
            {
                scope.Dispose();
                _references.Remove(actor);
            }
        }
    }
}
