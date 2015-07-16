//-----------------------------------------------------------------------
// <copyright file="NinjectDependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Akka.Actor;
using Akka.DI.Core;
using Ninject;
using Ninject.Activation.Blocks;

namespace Akka.DI.Ninject
{
    /// <summary>
    /// Provides services to the <see cref="ActorSystem "/> extension system
    /// used to create actors using the Ninject IoC container.
    /// </summary>
    public class NinjectDependencyResolver : IDependencyResolver
    {
        IKernel container;

        private ConcurrentDictionary<string, Type> typeCache;
        private ActorSystem system;
        private ConditionalWeakTable<ActorBase, IActivationBlock> references;

        /// <summary>
        /// Initializes a new instance of the <see cref="NinjectDependencyResolver"/> class.
        /// </summary>
        /// <param name="container">The container used to resolve references</param>
        /// <param name="system">The actor system to plug into</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="container"/> or the <paramref name="system"/> was null.
        /// </exception>
        public NinjectDependencyResolver(IKernel container, ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            this.system = system;
            this.system.AddDependencyResolver(this);
            this.references = new ConditionalWeakTable<ActorBase, IActivationBlock>();
        }

        /// <summary>
        /// Retrieves an actor's type with the specified name
        /// </summary>
        /// <param name="actorName">The name of the actor to retrieve</param>
        /// <returns>The type with the specified actor name</returns>
        public Type GetType(string actorName)
        {
            return typeCache.GetOrAdd(actorName, key => key.GetTypeValue());
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
                var block = container.BeginBlock();
                var actor = (ActorBase)block.Get(actorType);
                references.Add(actor, block);
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
            return system.GetExtension<DIExt>().Props(typeof(TActor));
        }

        /// <summary>
        /// Signals the DI container to release it's reference to the actor.
        /// <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor">The actor to remove from the container</param>
        public void Release(ActorBase actor)
        {
            IActivationBlock block;

            if (references.TryGetValue(actor, out block))
            {
                block.Dispose();
                references.Remove(actor);
            }
        }
    }
}
