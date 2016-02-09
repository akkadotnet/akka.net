using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.DI.Core;
using Akka.DI.TestKit;
using SimpleInjector;
using SimpleInjector.Advanced;
using SimpleInjector.Extensions.ExecutionContextScoping;

namespace Akka.DI.SimpleInjector.Tests
{
    public class SimpleInjectorDependencyResolverSpec : DiResolverSpec
    {
        protected override object NewDiContainer()
        {
            var container = new Container();
            container.Options.DefaultScopedLifestyle = new ExecutionContextScopeLifestyle();
            return container;
        }

        protected override IDependencyResolver NewDependencyResolver(object diContainer, ActorSystem system)
        {
            var container = ToContainer(diContainer);
            return new SimpleInjectorDependencyResolver(container, system);
        }

        protected override void Bind<T>(object diContainer, Func<T> generator)
        {
            var container = ToContainer(diContainer);
            container.Register(typeof(T), () => generator(), Lifestyle.Scoped);
        }

        protected override void Bind<T>(object diContainer)
        {
            var container = ToContainer(diContainer);
            container.Register(typeof(T), typeof(T), Lifestyle.Scoped);
        }

        private static Container ToContainer(object diContainer)
        {
            var container = diContainer as Container;
            Debug.Assert(container != null, "container != null");
            return container;
        }
    }


}
