﻿using Akka.Actor;
using Akka.DI.Core;
using Ninject;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.Ninject
{
    /// <summary>
    /// Provide services to ActorSystem Extension system used to create Actor
    /// using the Ninject IOC Container to handle wiring up dependencies to
    /// Actors
    /// </summary>
    public class NinjectDependencyResolver : IDependencyResolver
    {
       IKernel container;

        private ConcurrentDictionary<string, Type> typeCache;
        private ActorSystem system;

        /// <summary>
        /// NinjectDependencyResolver Constructor
        /// </summary>
        /// <param name="container">Instance IKernel</param>
        /// <param name="system">Instance to ActorSystem</param>
        public NinjectDependencyResolver(IKernel container, ActorSystem system)
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
            typeCache.TryAdd(actorName, actorName.GetTypeValue());

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

                return (ActorBase)container.GetService(actorType);
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

        /// <summary>
        /// This method is used to signal the DI Container that it can
        /// release it's reference to the actor.  <see href="http://www.amazon.com/Dependency-Injection-NET-Mark-Seemann/dp/1935182501/ref=sr_1_1?ie=UTF8&qid=1425861096&sr=8-1&keywords=mark+seemann">HERE</see> 
        /// </summary>
        /// <param name="actor"></param>
        public void Release(ActorBase actor)
        {
            container.Release(actor);
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
