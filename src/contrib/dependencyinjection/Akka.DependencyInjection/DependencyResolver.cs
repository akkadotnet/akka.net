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
    /// Provides users with immediate access to the <see cref="IDependencyResolver"/> bound to
    /// this <see cref="ActorSystem"/>, if any.
    /// </summary>
    public sealed class DependencyResolver : IExtension
    {
        public DependencyResolver(IDependencyResolver resolver)
        {
            Resolver = resolver;
        }

        /// <summary>
        /// The globally scoped <see cref="IDependencyResolver"/>.
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
        public IDependencyResolver Resolver { get; }

        public static DependencyResolver For(ActorSystem actorSystem)
        {
            return actorSystem.WithExtension<DependencyResolver, DependencyResolverExtension>();
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
            return Props(typeof(T), args);
        }
        
        public Props Props<T>() where T : ActorBase
        {
            return Props(typeof(T));
        }
        
        public Props Props(Type type)
        {
            return Resolver.Props(type);
        }
        
        public Props Props(Type type, params object[] args)
        {
            return Resolver.Props(type, args);
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public sealed class DependencyResolverExtension : ExtensionIdProvider<DependencyResolver>
    {
        public override DependencyResolver CreateExtension(ExtendedActorSystem system)
        {
            var setup = system.Settings.Setup.Get<DependencyResolverSetup>();
            if (setup.HasValue) return new DependencyResolver(setup.Value.DependencyResolver);
            
            var exception = new ConfigurationException("Unable to find [DependencyResolverSetup] included in ActorSystem settings." +
                                                       " Please specify one before attempting to use dependency injection inside Akka.NET.");
            system.EventStream.Publish(new Error(exception, "Akka.DependencyInjection", typeof(DependencyResolverExtension), exception.Message));
            throw exception;
        }
    }
}
