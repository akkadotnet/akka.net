//-----------------------------------------------------------------------
// <copyright file="DIActorSystemAdapter.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class represents an adapter used to generate <see cref="Akka.Actor.Props"/> configuration
    /// objects using the dependency injection (DI) extension using a given actor system.
    /// </summary>
    public class DIActorSystemAdapter
    {
        readonly DIExt producer;
        readonly ActorSystem system;

        /// <summary>
        /// Initializes a new instance of the <see cref="DIActorSystemAdapter"/> class.
        /// </summary>
        /// <param name="system">The actor system that contains the DI extension.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="system"/> is undefined.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when the Dependency Resolver has not been configured in the <see cref="ActorSystem" />.
        /// </exception>
        public DIActorSystemAdapter(ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system), $"DIActorSystemAdapter requires {nameof(system)} to be provided");
            this.system = system;
            this.producer = system.GetExtension<DIExt>();
            if (producer == null) throw new InvalidOperationException("The Dependency Resolver has not been configured yet");
        }

        /// <summary>
        /// Creates a <see cref="Akka.Actor.Props"/> configuration object for a given actor type.
        /// </summary>
        /// <param name="actorType">The actor type for which to create the <see cref="Akka.Actor.Props"/> configuration.</param>
        /// <returns>A <see cref="Akka.Actor.Props"/> configuration object for the given actor type.</returns>
        public Props Props(Type actorType) 
        {
            return producer.Props(actorType);
        }

        /// <summary>
        /// Creates a <see cref="Akka.Actor.Props"/> configuration object for a given actor type.
        /// </summary>
        /// <typeparam name="TActor">The actor type for which to create the <see cref="Akka.Actor.Props"/> configuration.</typeparam>
        /// <returns>A <see cref="Akka.Actor.Props"/> configuration object for the given actor type.</returns>
        public Props Props<TActor>() where TActor : ActorBase
        {
            return Props(typeof(TActor));
        }
    }
}
