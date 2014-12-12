using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.DI.Core
{
    public class DIActorProducerClass : IndirectActorProducer
    {
        private IDependencyResolver dependencyResolver;
        private string actorName;
        readonly Func<ActorBase> actorFactory;

        public DIActorProducerClass(IDependencyResolver dependencyResolver,
                                    string actorName)
        {
            if (dependencyResolver == null) throw new ArgumentNullException("dependencyResolver");
            if (actorName == null) throw new ArgumentNullException("actorName");

            this.dependencyResolver = dependencyResolver;
            this.actorName = actorName;
            this.actorFactory = dependencyResolver.CreateActorFactory(actorName);
        }
        public Type ActorType
        {
            get { return this.dependencyResolver.GetType(this.actorName); }
        }

        public ActorBase Produce()
        {
            return actorFactory();
        }

    }
}
