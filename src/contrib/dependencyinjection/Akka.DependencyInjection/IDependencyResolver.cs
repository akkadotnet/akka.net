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
    public interface IDependencyResolver
    {
        IResolverScope CreateScope();
        object GetService<T>();
        object GetService(Type type);
        Props Props(Type type, params object[] args);
        Props Props(Type type);
    }
}