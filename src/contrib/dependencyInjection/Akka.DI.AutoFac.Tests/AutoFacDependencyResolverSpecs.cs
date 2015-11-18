//-----------------------------------------------------------------------
// <copyright file="AutoFacDependencyResolverSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.DI.Core;
using Akka.DI.TestKit;
using Autofac;

namespace Akka.DI.AutoFac.Tests
{
    public class AutoFacDependencyResolverSpec : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            var builder = new ContainerBuilder();
            return builder;
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var builder = ToBuilder(diContainer);
            var container =  builder.Build();
            return new AutoFacDependencyResolver(container, system);
        }

        private static ContainerBuilder ToBuilder(object diContainer)
        {
            var builder = diContainer as ContainerBuilder;
            Debug.Assert(builder != null, "builder != null");
            return builder;
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var builder = ToBuilder(diContainer);
            builder.Register(c => generator.Invoke()).As<T>();
        }

        protected override void Bind<T>(object diContainer)
        {
            var builder = ToBuilder(diContainer);
            builder.RegisterType<T>();
        }
    }
}
