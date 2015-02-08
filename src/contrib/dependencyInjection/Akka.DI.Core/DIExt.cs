using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    /// <summary>
    /// Dependency Injection Extension used by the Actor System to Create the Prop configuration of DIActorProducer
    /// </summary>
    public class DIExt : IExtension
    {
        private IDependencyResolver dependencyResolver;

        /// <summary>
        /// Used to initialize the DIExtensionProvider
        /// </summary>
        /// <param name="dependencyResolver"></param>
        public void Initialize(IDependencyResolver dependencyResolver)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            this.dependencyResolver = dependencyResolver;
        }
        public Props Props(String actorName)
        {
            return new Props(typeof(DIActorProducer), new object[] { dependencyResolver, actorName });
        }

    }
}
