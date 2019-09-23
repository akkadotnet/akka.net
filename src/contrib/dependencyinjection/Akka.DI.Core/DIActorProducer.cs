﻿//-----------------------------------------------------------------------
// <copyright file="DIActorProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
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
        private readonly DIActorSystemAdapter.IDIProperties _diProperties;

        readonly Func<ActorBase> actorFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="DIActorProducer"/> class.
        /// </summary>
        /// <param name="dependencyResolver">The resolver used to resolve the given actor type.</param>
        /// <param name="actorType">The type of actor that this producer creates.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the specified <paramref name="dependencyResolver"/> or the specified <paramref name="actorType"/> is undefined.
        /// </exception>
        public DIActorProducer(IDependencyResolver dependencyResolver, Type actorType, DIActorSystemAdapter.IDIProperties diProperties)
        {
            if (dependencyResolver == null) throw new ArgumentNullException(nameof(dependencyResolver), $"DIActorProducer requires {nameof(dependencyResolver)} to be provided");
            if (actorType == null) throw new ArgumentNullException(nameof(actorType), $"DIActorProducer requires {nameof(actorType)} to be provided");

            this.dependencyResolver = dependencyResolver;
            this.actorType = actorType;
            this._diProperties = diProperties;
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
            var actor = actorFactory();

            // set additional properties here
            // independent of DI provider
            if (_diProperties != null)
            {
                foreach (var propertyEntry in _diProperties.Properties)
                {
                    var name = propertyEntry.Key;
                    var value = propertyEntry.Value;

                    var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy;
                    var propertyInfo = actorType.GetProperty(name, flags);
                    if (propertyInfo == null)
                        continue; // ignore or throw exception?

                    propertyInfo.SetValue(actor, value);
                }
            }

            return actor;
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
