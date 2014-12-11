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
        readonly Func<ActorBase> myActor;

        public DIActorProducerClass(IContainerConfiguration applicationContext,
                                    string actorName)
        {
            this.applicationContext = applicationContext;
            this.actorName = actorName;
            this.myActor = applicationContext.CreateActor(actorName);
        }
        public Type ActorType
        {
            get { return this.applicationContext.GetType(this.actorName); }
        }

        public ActorBase Produce()
        {
            return myActor();
        }

    }
}
