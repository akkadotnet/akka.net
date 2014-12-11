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
        private IContainerConfiguration applicationContext;

        public void Initialize(IContainerConfiguration applicationContext)
        {
            this.applicationContext = applicationContext;
        }
        public Props Props(String actorName)
        {
            return new Props(typeof(DIActorProducerClass), new object[] { applicationContext, actorName });
        }

    }
}
