//-----------------------------------------------------------------------
// <copyright file="StructureMapResolverSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.DI.Core;
using Akka.DI.TestKit;
using StructureMap;

namespace Akka.DI.StructureMap.Tests
{
    public class StructureMapResolverSpec : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            return new Container();
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var structureMap = diContainer as IContainer;
            return new StructureMapDependencyResolver(structureMap, system);
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var structureMap = ToContainer(diContainer);
            structureMap.Configure(cfg => cfg.For<T>().Use(ctx => generator()));
        }

        protected override void Bind<T>(object diContainer)
        {
            var structureMap = ToContainer(diContainer);
            structureMap.Configure(cfg => cfg.For<T>());
        }

        private static IContainer ToContainer(object diContainer)
        {
            var structureMap = diContainer as IContainer;
            Debug.Assert(structureMap != null, "structureMap != null");
            return structureMap;
        }
    }
}
