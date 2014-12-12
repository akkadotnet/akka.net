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
        private IContainerConfiguration applicationContext;
        private string actorName;
        readonly Func<ActorBase> actorFactory;

        public DIActorProducerClass(IContainerConfiguration applicationContext,
                                    string actorName)
        {
            this.applicationContext = applicationContext;
            this.actorName = actorName;
            this.actorFactory = applicationContext.CreateActorFactory(actorName);
        }
        public Type ActorType
        {
            get { return this.applicationContext.GetType(this.actorName); }
        }

        public ActorBase Produce()
        {
            return actorFactory();
        }

    }
}
