// //-----------------------------------------------------------------------
// // <copyright file="ServiceProviderScope.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;

namespace Akka.DependencyInjection
{
    public interface IResolverScope : IDisposable
    {
        IDependencyResolver Resolver { get; }
    }
    
    public class ServiceProviderScope : IResolverScope
    {
        private readonly IServiceScope _scope;
        public IDependencyResolver Resolver { get; }
        public ServiceProviderScope(IServiceScope scope)
        {
            _scope = scope;
            Resolver = new ServiceProviderDependencyResolver(scope.ServiceProvider);
        }

        public void Dispose()
        {
            _scope?.Dispose();
        }
    }
}