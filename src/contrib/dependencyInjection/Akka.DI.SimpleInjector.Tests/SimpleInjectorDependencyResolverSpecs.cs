using System;
using Akka.Actor;
using Akka.DI.Core;
using Akka.DI.TestKit;
using SimpleInjector;
using SimpleInjector.Extensions.ExecutionContextScoping;

namespace Akka.DI.SimpleInjector.Tests
{
    public class SimpleInjectorDependencyResolverSpecs : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            var container = new Container();
            container.Options.DefaultScopedLifestyle = new ExecutionContextScopeLifestyle();           
            return container;
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var container = diContainer as Container;
            return new SimpleInjectorDependencyResolver(container, system);
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var container = diContainer as Container;
            container.Register(generator, Lifestyle.Scoped);
        }

        protected override void Bind<T>(object diContainer)
        {
            var container = diContainer as Container;
            container.Register<T>(Lifestyle.Scoped);
        }
    }
}
