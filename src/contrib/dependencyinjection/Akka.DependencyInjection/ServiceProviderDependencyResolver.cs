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
        
        public object GetService<T>()
        {
            return ServiceProvider.GetService<T>();
        }
        
        public object GetService(Type type)
        {
            return ServiceProvider.GetService(type);
        }
        
        public Props Props(Type type, params object[] args)
        {
            return new ServiceProviderProps(ServiceProvider, type, args);
        }
        
        public Props Props(Type type)
        {
            return new ServiceProviderProps(ServiceProvider, type);
        }
    }

    
}