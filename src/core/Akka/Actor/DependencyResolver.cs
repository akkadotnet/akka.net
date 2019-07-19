#region copyright
//-----------------------------------------------------------------------
// <copyright file="DependencyResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
#endregion

using System;
using Akka.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.Actor
{
    /// <summary>
    /// A <see cref="DependencyResolver"/> is a class, which can be overriden by the user in order to be used as a point,
    /// when custom dependencies can be registered at the moment when <see cref="ActorSystem"/> is being created.
    /// </summary>
    public class DependencyResolver
    {
        /// <summary>
        /// Determines a particular service provider factory implementation to be used.
        /// </summary>
        public virtual IServiceProviderFactory<IServiceCollection> ServiceProviderFactory(ExtendedActorSystem system)
        {
            return new DefaultServiceProviderFactory();
        }

        /// <summary>
        /// Extension point, where user can configure service dependencies, which will be injected during actor creation
        /// via dynamic Props model.
        /// </summary>
        public virtual void ConfigureDependencies(ExtendedActorSystem system, IServiceCollection services)
        {
            services.AddSingleton<ActorSystem>(system);
            services.AddSingleton<ExtendedActorSystem>(system);
        }
    }
}