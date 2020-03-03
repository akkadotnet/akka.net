//-----------------------------------------------------------------------
// <copyright file="DIActorContextAdapter.cs" company="Akka.NET Project">
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
    /// objects using the dependency injection (DI) extension using a given actor context.
    /// </summary>
    public class DIActorContextAdapter
    {
        readonly DIExt producer;
        readonly IActorContext context;

        /// <summary>
        /// Initializes a new instance of the <see cref="DIActorContextAdapter"/> class.
        /// </summary>
        /// <param name="context">The actor context associated with a system that contains the DI extension.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="context"/> is undefined.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// This exception is thrown when the Dependency Resolver has not been configured in the <see cref="ActorSystem" />.
        /// </exception>
        public DIActorContextAdapter(IActorContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context), $"DIActorContextAdapter requires {nameof(context)} to be provided");
            this.context = context;
            this.producer = context.System.GetExtension<DIExt>();
            if (producer == null) throw new InvalidOperationException("The Dependency Resolver has not been configured yet");
        }

        /// <summary>
        /// Obsolete. Use <see cref="Props(Type)"/> or <see cref="Props{TActor}"/> methods for actor creation. This method will be removed in future versions.
        /// </summary>
        /// <typeparam name="TActor">N/A</typeparam>
        /// <param name="name">N/A</param>
        /// <returns>N/A</returns>
        [Obsolete("Use Props methods for actor creation. This method will be removed in future versions")]
        public IActorRef ActorOf<TActor>(string name = null) where TActor : ActorBase
        {
            return context.ActorOf(producer.Props(typeof(TActor)), name);
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
