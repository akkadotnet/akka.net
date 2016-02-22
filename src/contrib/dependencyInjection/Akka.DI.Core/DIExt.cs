//-----------------------------------------------------------------------
// <copyright file="DIExt.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class represents an <see cref="ActorSystem"/> extension used to create <see cref="Akka.Actor.Props"/>
    /// configuration objects using a dependency injection (DI) container.
    /// </summary>
    public class DIExt : IExtension
    {
        private IDependencyResolver dependencyResolver;

        /// <summary>
        /// Initializes the extension to use a given DI resolver.
        /// </summary>
        /// <param name="dependencyResolver">The resolver used to resolve types from the DI container.</param>
        /// <exception cref="ArgumentNullException">The dependency resolver must not be null.</exception>
        public void Initialize(IDependencyResolver dependencyResolver)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver", "The dependency resolver must not be null.");
            this.dependencyResolver = dependencyResolver;
        }

        /// <summary>
        /// Creates a <see cref="Akka.Actor.Props"/> configuration object for a given actor type.
        /// </summary>
        /// <param name="actorType">The actor type for which to create the <see cref="Akka.Actor.Props"/> configuration.</param>
        /// <returns>A <see cref="Akka.Actor.Props"/> configuration object for the given actor type.</returns>
        public Props Props(Type actorType)
        {
            return new Props(typeof(DIActorProducer), new object[] { dependencyResolver, actorType });
        }
    }
}
