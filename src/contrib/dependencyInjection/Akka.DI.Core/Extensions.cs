using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.Core
{
    public static class Extensions
    {
        public static void AddDependencyResolver(this ActorSystem system, IDependencyResolver dependencyResolve)
        {
            if (system == null) throw new ArgumentNullException("system");
            system.RegisterExtension((IExtensionId)DIExtension.DIExtensionProvider);
            DIExtension.DIExtensionProvider.Get(system).Initialize(dependencyResolve);
        }
        
    }
}
