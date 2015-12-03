//-----------------------------------------------------------------------
// <copyright file="CastleWindsorResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.DI.Core;
using Akka.DI.TestKit;
using Castle.MicroKernel.Registration;
using Castle.Windsor;

namespace Akka.DI.CastleWindsor.Tests
{
    public class CastleWindsorResolverSpec : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            return new WindsorContainer();
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var windsor = GetContainer(diContainer);
            return new WindsorDependencyResolver(windsor, system);
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var windsor = GetContainer(diContainer);
            windsor.Register(Component.For(typeof(T)).UsingFactoryMethod(generator).LifestyleTransient());
        }

        protected override void Bind<T>(object diContainer)
        {
            var windsor = GetContainer(diContainer);
            windsor.Register(Component.For(typeof(T)).LifestyleTransient());
        }

        private static IWindsorContainer GetContainer(object diContainer)
        {
            var windsor = diContainer as IWindsorContainer;
            Debug.Assert(windsor != null, "windsor != null");
            return windsor;
        }
    }
}
