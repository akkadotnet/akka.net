//-----------------------------------------------------------------------
// <copyright file="SimpleInjectorDependencyResolver.cs" company="Akka.NET Project">
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
using SimpleInjector;
using SimpleInjector.Extensions.ExecutionContextScoping;
using Scope = SimpleInjector.Scope;

namespace Akka.DI.SimpleInjector
{
    /// <summary>
    /// Provides services to the <see cref="ActorSystem "/> extension _system
    /// used to create actors using the SimpleInject IoC _container.
    /// </summary>
    public class SimpleInjectorDependencyResolver : IDependencyResolver
    {
        private readonly Container _container;
        private readonly ConcurrentDictionary<string, Type> _typeCache;
        private readonly ActorSystem _system;
        private readonly ConditionalWeakTable<ActorBase, Scope> _references;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleInjectorDependencyResolver"/> class.
        /// </summary>
        /// <param name="container">The _container used to resolve _references</param>
        /// <param name="system">The actor _system to plug into</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="container"/> or the <paramref name="system"/> was null.
        /// </exception>
        public SimpleInjectorDependencyResolver(Container container, ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (container == null) throw new ArgumentNullException("container");
            this._container = container;
            _typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            this._system = system;
            this._system.AddDependencyResolver(this);
            this._references = new ConditionalWeakTable<ActorBase, Scope>();
        }

        /// <summary>
        /// Retrieves an actor's type with the specified name
        /// </summary>
        /// <param name="actorName">The name of the actor to retrieve</param>
        /// <returns>The type with the specified actor name</returns>
        public Type GetType(string actorName)
        {
            _typeCache.
                TryAdd(actorName,
                  //     actorName.GetTypeValue() ??
                       _container.GetRootRegistrations().
                       Where(registration => registration.ServiceType
                                 .Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase)).
                        Select(registration => registration.ServiceType).
                        FirstOrDefault());

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
                var scope =_container.BeginExecutionContextScope();
                
                var actor = (ActorBase) _container.GetInstance(actorType);

                _references.Add(actor, scope);
                return actor;
            };
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
            return _system.GetExtension<DIExt>().Props(actorType);
        }

        /// <summary>
        /// Signals the _container to release it's reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to remove from the _container</param>
        public void Release(ActorBase actor)
        {
            Scope scope;

            if (_references.TryGetValue(actor, out scope))

                _references.Remove(actor);
                scope.Dispose();
            }
        }
}
