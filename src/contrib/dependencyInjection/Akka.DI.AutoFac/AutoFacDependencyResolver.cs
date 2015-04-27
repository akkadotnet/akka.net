﻿//-----------------------------------------------------------------------
// <copyright file="AutoFacDependencyResolver.cs" company="Akka.NET Project">
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
using Autofac;

namespace Akka.DI.AutoFac
{
    /// <summary>
    /// Provide services to ActorSystem Extension system used to create Actor
    /// using the AutoFac IOC Container to handle wiring up dependencies to
    /// Actors
    /// </summary>
    public class AutoFacDependencyResolver : IDependencyResolver
    {
        private ILifetimeScope container;
        private ConcurrentDictionary<string, Type> typeCache;
        private ActorSystem system;
        private ConditionalWeakTable<ActorBase, ILifetimeScope> references;

        /// <summary>
        /// AutoFacDependencyResolver Constructor
        /// </summary>
        /// <param name="rootScope">Instance to the AutoFac IContainer</param>
        /// <param name="system">Instance of the ActorSystem</param>
        public AutoFacDependencyResolver(ILifetimeScope rootScope, ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (rootScope == null) throw new ArgumentNullException("container");
            this.container = rootScope;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            this.system = system;
            this.system.AddDependencyResolver(this);
            this.references = new ConditionalWeakTable<ActorBase, ILifetimeScope>();
        }

        /// <summary>
        /// Returns the Type for the Actor Type specified in the actorName
        /// </summary>
        /// <param name="actorName"></param>
        /// <returns></returns>
        public Type GetType(string actorName)
        {
     
            typeCache.
                TryAdd(actorName,
                       actorName.GetTypeValue() ??
                       container.
                       ComponentRegistry.
                       Registrations.
                       Where(registration => registration.Activator.LimitType.
                                 Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase)).
                        Select(registration => registration.Activator.LimitType).
                        FirstOrDefault());

            return typeCache[actorName];

        }
        /// <summary>
        /// Creates a delegate factory based on the actorName
        /// </summary>
        /// <param name="actorName">Name of the ActorType</param>
        /// <returns>factory delegate</returns>
        public Func<ActorBase> CreateActorFactory(Type actorType)
        {
            return () =>
            {
                var scope = container.BeginLifetimeScope();
                var actor = (ActorBase)scope.Resolve(actorType);
                references.Add(actor, scope);
                return actor;
            };
        }
        /// <summary>
        /// Used Register the Configuration for the ActorType specified in TActor
        /// </summary>
        /// <typeparam name="TActor">Tye of Actor that needs to be created</typeparam>
        /// <returns>Props configuration instance</returns>
        public Props Create<TActor>() where TActor : ActorBase
        {
            return system.GetExtension<DIExt>().Props(typeof(TActor));
        }

        /// <summary>
        /// This method is used to signal the DI Container that it can
        /// release it's reference to the actor.  <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor"></param>

        public void Release(ActorBase actor)
        {
            ILifetimeScope scope;

            if (references.TryGetValue(actor, out scope))
            {
                scope.Dispose();
                references.Remove(actor);
            }
        }
    }
}
