using System;
using System.Diagnostics;
using Akka.Actor;
using Akka.DI.TestKit;
using Akka.DI.Core;
using LightInject;

namespace Akka.DI.LightInject.Tests
{
    public class LightInjectDependencyResolverSpec : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            return new ServiceContainer() as IServiceContainer;
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var container = ToIServiceContainer(diContainer);
            return new LightInjectDependencyResolver(container, system);
        }

        protected override void Bind<T>(object diContainer)
        {
            var container = ToIServiceContainer(diContainer);
            container.Register<T>(new PerScopeLifetime());
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var container = ToIServiceContainer(diContainer);
            container.Register(f => generator(), new PerScopeLifetime());
        }

        private static IServiceContainer ToIServiceContainer(object container)
        {
            var c = container as IServiceContainer;
            Debug.Assert(c != null, "c != null");
            return c;
        }
    }
}
