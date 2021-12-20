// //-----------------------------------------------------------------------
// // <copyright file="ServiceProviderDependencyResolver.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.DependencyInjection
{
    /// <summary>
    /// INTERNAL API.
    ///
    /// <see cref="IDependencyResolver"/> implementation backed by <see cref="IServiceProvider"/>
    /// </summary>
    public class ServiceProviderDependencyResolver : IDependencyResolver
    {
        public IServiceProvider ServiceProvider { get; }

        public ServiceProviderDependencyResolver(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IResolverScope CreateScope()
        {
            return new ServiceProviderScope(ServiceProvider.CreateScope());
        }
        
        public T GetService<T>()
        {
            return ServiceProvider.GetService<T>();
        }
        
        public object GetService(Type type)
        {
            return ServiceProvider.GetService(type);
        }
        
        public Props Props(Type type, params object[] args)
        {
            if(typeof(ActorBase).IsAssignableFrom(type))
                return Akka.Actor.Props.CreateBy(new ServiceProviderActorProducer(ServiceProvider, type, args));
            throw new ArgumentException(nameof(type), $"[{type}] does not implement Akka.Actor.ActorBase.");
        }
        
        public Props Props(Type type)
        {
            return Props(type, Array.Empty<object>());
        }

        public Props Props<T>(params object[] args) where T : ActorBase
        {
            return Akka.Actor.Props.CreateBy(new ServiceProviderActorProducer<T>(ServiceProvider, args));
        }
    }

    
}
