//-----------------------------------------------------------------------
// <copyright file="DIExt.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

namespace Akka.DI.Core
{
    /// <summary>
    /// Dependency Injection Extension used by the Actor System to Create the Prop configuration of DIActorProducer
    /// </summary>
    public class DIExt : IExtension
    {
        private IDependencyResolver dependencyResolver;

        /// <summary>
        /// Used to initialize the DIExtensionProvider
        /// </summary>
        /// <param name="dependencyResolver"></param>
        public void Initialize(IDependencyResolver dependencyResolver)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            this.dependencyResolver = dependencyResolver;
        }
        public Props Props(String actorName)
        {
            return new Props(typeof(DIActorProducer), new object[] { dependencyResolver, actorName });
        }

    }
}
