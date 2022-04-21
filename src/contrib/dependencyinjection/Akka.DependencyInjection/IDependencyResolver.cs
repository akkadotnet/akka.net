// //-----------------------------------------------------------------------
// // <copyright file="IDependencyResolver.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DependencyInjection
{
    /// <summary>
    /// Interface abstraction for working with DI providers
    /// in Akka.NET without being bound to any specific implementation.
    /// </summary>
    /// <remarks>
    /// See <see cref="ServiceProviderDependencyResolver"/> for a reference implementation.
    /// </remarks>
    public interface IDependencyResolver
    {
        IResolverScope CreateScope();
        T GetService<T>();
        object GetService(Type type);

        /// <summary>
        /// Used to dynamically instantiate an actor where some of the constructor arguments are populated via dependency injection
        /// and others are not.
        /// </summary>
        /// <remarks>
        /// YOU ARE RESPONSIBLE FOR MANAGING THE LIFECYCLE OF YOUR OWN DEPENDENCIES. AKKA.NET WILL NOT ATTEMPT TO DO IT FOR YOU.
        /// </remarks>
        /// <param name="type">The type of actor to instantiate.</param>
        /// <param name="args">Optional. Any constructor arguments that will be passed into the actor's constructor directly without being resolved by DI first.</param>
        /// <returns>A new <see cref="Akka.Actor.Props"/> instance which uses DI internally.</returns>
        Props Props(Type type, params object[] args);

        /// <summary>
        /// Used to dynamically instantiate an actor where some of the constructor arguments are populated via dependency injection
        /// and others are not.
        /// </summary>
        /// <remarks>
        /// YOU ARE RESPONSIBLE FOR MANAGING THE LIFECYCLE OF YOUR OWN DEPENDENCIES. AKKA.NET WILL NOT ATTEMPT TO DO IT FOR YOU.
        /// </remarks>
        /// <param name="type">The type of actor to instantiate.</param>
        /// <returns>A new <see cref="Akka.Actor.Props"/> instance which uses DI internally.</returns>
        Props Props(Type type);

        /// <summary>
        /// Used to dynamically instantiate an actor where some of the constructor arguments are populated via dependency injection
        /// and others are not.
        /// </summary>
        /// <remarks>
        /// YOU ARE RESPONSIBLE FOR MANAGING THE LIFECYCLE OF YOUR OWN DEPENDENCIES. AKKA.NET WILL NOT ATTEMPT TO DO IT FOR YOU.
        /// </remarks>
        /// <typeparam name="T">The type of actor to instantiate.</typeparam>
        /// <param name="args">Optional. Any constructor arguments that will be passed into the actor's constructor directly without being resolved by DI first.</param>
        /// <returns>A new <see cref="Akka.Actor.Props"/> instance which uses DI internally.</returns>
        Props Props<T>(params object[] args) where T : ActorBase;
    }
}