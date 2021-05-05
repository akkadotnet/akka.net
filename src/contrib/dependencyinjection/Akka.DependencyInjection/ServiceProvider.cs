//-----------------------------------------------------------------------
// <copyright file="ServiceProvider.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.DependencyInjection
{
    /// <summary>
    /// Provides users with immediate access to the <see cref="IServiceProvider"/> bound to
    /// this <see cref="ActorSystem"/>, if any.
    /// </summary>
    public sealed class ServiceProvider : IExtension
    {
        public ServiceProvider(IServiceProvider provider)
        {
            Provider = provider;
        }

        /// <summary>
        /// The globally scoped <see cref="IServiceProvider"/>.
        /// </summary>
        /// <remarks>
        /// Per https://docs.microsoft.com/en-us/dotnet/core/extensions/dependency-injection-guidelines - please use
        /// the appropriate <see cref="IServiceScope"/> for your actors and the dependencies they consume. DI is typically
        /// not used for long-lived, stateful objects such as actors. 
        ///
        /// Therefore, injecting transient dependencies via constructors is a bad idea in most cases. You'd be far better off
        /// creating a local "request scope" each time your actor processes a message that depends on a transient dependency,
        /// such as a database connection, and disposing that scope once the operation is complete.
        ///
        /// Actors are not MVC Controllers. Actors can live forever, have the ability to restart, and are often stateful.
        /// Be mindful of this as you use this feature or bad things will happen. Akka.NET does not magically manage scopes
        /// for you.
        /// </remarks>
        public IServiceProvider Provider { get; }

        public static ServiceProvider For(ActorSystem actorSystem)
        {
            return actorSystem.WithExtension<ServiceProvider, ServiceProviderExtension>();
        }

        /// <summary>
        /// Uses a delegate to dynamically instantiate an actor where some of the constructor arguments are populated via dependency injection
        /// and others are not.
        /// </summary>
        /// <remarks>
        /// YOU ARE RESPONSIBLE FOR MANAGING THE LIFECYCLE OF YOUR OWN DEPENDENCIES. AKKA.NET WILL NOT ATTEMPT TO DO IT FOR YOU.
        /// </remarks>
        /// <typeparam name="T">The type of actor to instantiate.</typeparam>
        /// <param name="args">Optional. Any constructor arguments that will be passed into the actor's constructor directly without being resolved by DI first.</param>
        /// <returns>A new <see cref="Akka.Actor.Props"/> instance which uses DI internally.</returns>
        public Props Props<T>(params object[] args) where T : ActorBase
        {
            return Akka.Actor.Props.CreateBy(new ServiceProviderActorProducer<T>(Provider, args));
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class ServiceProviderExtension : ExtensionIdProvider<ServiceProvider>
    {
        public override ServiceProvider CreateExtension(ExtendedActorSystem system)
        {
            var setup = system.Settings.Setup.Get<ServiceProviderSetup>();
            if (!setup.HasValue)
            {
                var exception = new ConfigurationException("Unable to find [ServiceProviderSetup] included in ActorSystem settings." +
                                                           " Please specify one before attempting to use dependency injection inside Akka.NET.");
                system.EventStream.Publish(new Error(exception, "Akka.DependencyInjection", typeof(ServiceProviderExtension), exception.Message));
                throw exception;
            }

            return new ServiceProvider(setup.Value.ServiceProvider);
        }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// Used to create actors via the <see cref="ActivatorUtilities"/>.
    /// </summary>
    /// <typeparam name="TActor">the actor type</typeparam>
    internal sealed class ServiceProviderActorProducer<TActor> : IIndirectActorProducer where TActor:ActorBase
    {
        private readonly IServiceProvider _provider;
        private readonly object[] _args;

        public ServiceProviderActorProducer(IServiceProvider provider, object[] args)
        {
            _provider = provider;
            _args = args;
            ActorType = typeof(TActor);
        }

        public ActorBase Produce()
        {
            return (ActorBase)ActivatorUtilities.CreateInstance(_provider, ActorType, _args);
        }

        public Type ActorType { get; }

        public void Release(ActorBase actor)
        {
            // no-op
        }
    }
}
