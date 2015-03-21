using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.DI.Core
{
    /// <summary>
    /// Extension methods used to simplify working with the Akka.DI.Core
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Method used to register the IDependencyResolver to the ActorSystem
        /// </summary>
        /// <param name="system">Instance of the ActorSystem</param>
        /// <param name="dependencyResolver">Concrete Instance of IDependencyResolver i.e. Akka.DI.AutoFac.AutoFacDependencyResolver</param>
        public static void AddDependencyResolver(this ActorSystem system, IDependencyResolver dependencyResolver)
        {
            if (system == null) throw new ArgumentNullException("system");
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            system.RegisterExtension(DIExtension.DIExtensionProvider);
            DIExtension.DIExtensionProvider.Get(system).Initialize(dependencyResolver);
        }
        

        public static DIActorContextAdapter DI(this IActorContext context)
        {
            return new DIActorContextAdapter(context);
        }

    }
}
