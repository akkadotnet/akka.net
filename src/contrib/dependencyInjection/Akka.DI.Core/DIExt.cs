using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    public class DIExt : IExtension
    {
        private IDependencyResolver dependencyResolver;

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
