﻿//-----------------------------------------------------------------------
// <copyright file="Extensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// Extension methods used to simplify working with the Akka.DI.Core
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Method used to register the IDependencyResolver to the ActorSystem
        /// </summary>
        /// <param name="system">Instance of the ActorSystem</param>
        /// <param name="dependencyResolver">Concrete Instance of IDependencyResolver i.e. Akka.DI.AutoFac.AutoFacDependencyResolver</param>
        public static void AddDependencyResolver(this ActorSystem system, IDependencyResolver dependencyResolver)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            system.RegisterExtension(DIExtension.DIExtensionProvider);
            DIExtension.DIExtensionProvider.Get(system).Initialize(dependencyResolver);
        }
        

        public static DIActorContextAdapter DI(this IActorContext context)
        {
            return new DIActorContextAdapter(context);
        }

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

