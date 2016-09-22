//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// This class contains extension methods used to simplify working with dependency injection (DI) inside an <see cref="ActorSystem"/>.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Registers a dependency resolver with a given actor system.
        /// </summary>
        /// <param name="system">The actor system in which to register the given dependency resolver.</param>
        /// <param name="dependencyResolver">The dependency resolver being registered to the actor system.</param>
        /// <exception cref="ArgumentNullException">
        /// Either the <paramref name="system"/> or the <paramref name="dependencyResolver"/> was null.
        /// </exception>
        public static void AddDependencyResolver(this ActorSystem system, IDependencyResolver dependencyResolver)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            system.RegisterExtension(DIExtension.DIExtensionProvider);
            DIExtension.DIExtensionProvider.Get(system).Initialize(dependencyResolver);
        }

        /// <summary>
        /// Creates an adapter used to generate <see cref="Akka.Actor.Props"/> configuration objects using the DI extension using a given actor system.
        /// </summary>
        /// <param name="system">The actor system that contains the DI extension.</param>
        /// <returns>An adapter used to generate <see cref="Akka.Actor.Props"/> configuration objects using the DI extension.</returns>
        public static DIActorSystemAdapter DI(this ActorSystem system)
        {
            return new DIActorSystemAdapter(system);
        }

        /// <summary>
        /// Creates an adapter used to generate <see cref="Akka.Actor.Props"/> configuration objects using the DI extension using a given actor context.
        /// </summary>
        /// <param name="context">The actor context associated with a system that contains the DI extension.</param>
        /// <returns>An adapter used to generate <see cref="Akka.Actor.Props"/> configuration objects using the DI extension.</returns>
        public static DIActorContextAdapter DI(this IActorContext context)
        {
            return new DIActorContextAdapter(context);
        }

        /// <summary>
        /// Retrieves the <see cref="Type"/> with a given name from the current <see cref="AppDomain"/>.
        /// </summary>
        /// <param name="typeName">The string representation of the type to retrieve.</param>
        /// <returns>The <see cref="Type"/> with the given name.</returns>
        public static Type GetTypeValue(this string typeName)
        {
            var firstTry = Type.GetType(typeName);
            Func<Type> searchForType = () =>
                AppDomain.CurrentDomain
                    .GetAssemblies()
                    .SelectMany(x => x.GetTypes())
                    .FirstOrDefault(t => t.Name.Equals(typeName));
            
            return firstTry ?? searchForType();
        }
    }
}
