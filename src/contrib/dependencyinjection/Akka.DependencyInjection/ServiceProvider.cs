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

        ///// <summary>
        ///// Uses a delegate to dynamically instantiate an actor where some of the constructor arguments are populated via dependency injection
        ///// and others are not.
        ///// </summary>
        ///// <remarks>
        ///// YOU ARE RESPONSIBLE FOR MANAGING THE LIFECYCLE OF YOUR OWN DEPENDENCIES. AKKA.NET WILL NOT ATTEMPT TO DO IT FOR YOU.
        ///// </remarks>
        ///// <typeparam name="T">The type of actor to instantiate.</typeparam>
        ///// <param name="producer">The delegate used to create a new instance of your actor type.</param>
        ///// <returns>A new <see cref="Props"/> instance which uses DI internally.</returns>
        //public Props Props<T>(Func<IServiceProvider, T> producer) where T : ActorBase
        //{
        //    return new ServiceProviderProps<T>(producer, Provider);
        //}

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
            return new ServiceProviderProps<T>(Provider, args);
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
    /// This class represents a specialized <see cref="Akka.Actor.Props"/> that uses delegate invocation
    /// to create new actor instances, rather than a traditional <see cref="System.Activator"/>.
    ///
    /// Relies on having an active <see cref="IServiceProvider"/> implementation available
    /// </summary>
    /// <typeparam name="TActor">The type of the actor to create.</typeparam>
    internal class ServiceProviderProps<TActor> : Props where TActor : ActorBase
    {
        private readonly IServiceProvider _provider;

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceProviderProps{TActor}" /> class.
        /// </summary>
        /// <param name="provider">The <see cref="IServiceProvider"/> used to power this class</param>
        /// <param name="args">The constructor arguments passed to the actor's constructor.</param>
        public ServiceProviderProps(IServiceProvider provider, params object[] args)
            : base(typeof(TActor), args)
        {
            _provider = provider;
        }

        /// <summary>
        /// Creates a new actor using the configured factory method.
        /// </summary>
        /// <returns>The actor created using the factory method.</returns>
        public override ActorBase NewActor()
        {
            return ActivatorUtilities.CreateInstance<TActor>(_provider, Arguments);
        }

        #region Copy methods

        /// <summary>
        /// Creates a copy of the current instance.
        /// </summary>
        /// <returns>The newly created <see cref="Akka.Actor.Props"/></returns>
        protected override Props Copy()
        {
            return new ServiceProviderProps<TActor>(_provider, Arguments)
            {
                Deploy = Deploy,
                SupervisorStrategy = SupervisorStrategy
            };
        }

        #endregion
    }
}
