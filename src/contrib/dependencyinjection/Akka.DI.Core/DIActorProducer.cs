//-----------------------------------------------------------------------
// <copyright file="DIActorProducer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class represents an actor creation strategy that uses dependency injection (DI) to resolve and instantiate actors based on their type.
    /// </summary>
    public sealed class DIActorProducer : IIndirectActorProducerWithActorType
    {
        private readonly IDependencyResolver _dependencyResolver;
        private readonly Type _actorType;
        private readonly Func<ActorBase> _actorFactory;

        public Type ActorType => _actorType;

        /// <summary>
        /// Initializes a new instance of the <see cref="DIActorProducer"/> class.
        /// </summary>
        /// <param name="dependencyResolver">The resolver used to resolve the given actor type.</param>
        /// <param name="actorType">The type of actor that this producer creates.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when either the specified <paramref name="dependencyResolver"/> or the specified <paramref name="actorType"/> is undefined.
        /// </exception>
        public DIActorProducer(IDependencyResolver dependencyResolver, Type actorType)
        {
            if (dependencyResolver == null) throw new ArgumentNullException(nameof(dependencyResolver), $"DIActorProducer requires {nameof(dependencyResolver)} to be provided");
            if (actorType == null) throw new ArgumentNullException(nameof(actorType), $"DIActorProducer requires {nameof(actorType)} to be provided");

            _dependencyResolver = dependencyResolver;
            _actorType = actorType;
            _actorFactory = dependencyResolver.CreateActorFactory(actorType);
        }
        
        /// <summary>
        /// Creates an actor based on the container's implementation specific actor factory.
        /// </summary>
        /// <returns>An actor created by the container.</returns>
        public ActorBase Produce(Props props)
        {
            if (props.Type != _actorType)
                throw new InvalidOperationException($"invalid actor type {props.Type}");
            return _actorFactory();
        }

        /// <summary>
        /// Signals the container that it can release its reference to the actor.
        /// </summary>
        /// <param name="actor">The actor to remove from the container.</param>
        public void Release(ActorBase actor)
        {
            _dependencyResolver.Release(actor);
        }
    }
}
