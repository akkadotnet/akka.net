using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akka.Routing
{
    public class ResizablePoolActor : RouterActor
    {
        public ResizablePoolActor(SupervisorStrategy supervisorStrategy) 
        {
            this.supervisorStrategy = supervisorStrategy;
        }

        private SupervisorStrategy supervisorStrategy;
        protected override SupervisorStrategy SupervisorStrategy()
        {
            return this.supervisorStrategy;
        }
    }
}
