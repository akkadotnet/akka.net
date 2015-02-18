using Akka.Actor;
using Akka.DI.Core;
using Autofac;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.AutoFac
{
    /// <summary>
    /// Provide services to ActorSystem Extension system used to create Actor
    /// using the AutoFac IOC Container to handle wiring up dependencies to
    /// Actors
    /// </summary>
    public class AutoFacDependencyResolver : IDependencyResolver
    {
        private IContainer container;
        private ConcurrentDictionary<string, Type> typeCache;
        private ActorSystem system;

        /// <summary>
        /// AutoFacDependencyResolver Constructor
        /// </summary>
        /// <param name="container">Instance to the AutoFac IContainer</param>
        /// <param name="system">Instance of the ActorSystem</param>
        public AutoFacDependencyResolver(IContainer container, ActorSystem system)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (container == null) throw new ArgumentNullException("container");
            this.container = container;
            typeCache = new ConcurrentDictionary<string, Type>(StringComparer.InvariantCultureIgnoreCase);
            this.system = system;
            this.system.AddDependencyResolver(this);
        }

        /// <summary>
        /// Returns the Type for the Actor Type specified in the actorName
        /// </summary>
        /// <param name="actorName"></param>
        /// <returns></returns>
        public Type GetType(string actorName)
        {
     
            typeCache.
                TryAdd(actorName,
                       actorName.GetTypeValue() ??
                       container.
                       ComponentRegistry.
                       Registrations.
                       Where(registration => registration.Activator.LimitType.
                                 Name.Equals(actorName, StringComparison.InvariantCultureIgnoreCase)).
                        Select(registration => registration.Activator.LimitType).
                        FirstOrDefault());

            return typeCache[actorName];

        }
        /// <summary>
        /// Creates a delegate factory based on the actorName
        /// </summary>
        /// <param name="actorName">Name of the ActorType</param>
        /// <returns>factory delegate</returns>
        public Func<ActorBase> CreateActorFactory(string actorName)
        {
            return () =>
            {
                Type actorType = this.GetType(actorName);
                return (ActorBase)container.Resolve(actorType);
            };
        }
        /// <summary>
        /// Used Register the Configuration for the ActorType specified in TActor
        /// </summary>
        /// <typeparam name="TActor">Tye of Actor that needs to be created</typeparam>
        /// <returns>Props configuration instance</returns>
        public Props Create<TActor>() where TActor : ActorBase
        {
            return system.GetExtension<DIExt>().Props(typeof(TActor).Name);
        }

    }
    internal static class Extensions
    {
        public static Type GetTypeValue(this string typeName)
        {
            var firstTry = Type.GetType(typeName);
            Func<Type> searchForType = () =>
            {
                return
                AppDomain.
                    CurrentDomain.
                    GetAssemblies().
                    SelectMany(x => x.GetTypes()).
                    Where(t => t.Name.Equals(typeName)).
                    FirstOrDefault();
            };
            return firstTry ?? searchForType();
        }

    }
}
