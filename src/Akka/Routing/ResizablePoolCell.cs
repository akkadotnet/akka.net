using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Dispatch;

namespace Akka.Routing
{
    internal class ResizablePoolCell : RoutedActorCell
    {
        private Resizer resizer;
        private volatile bool resizeInProgress;
        private long resizeCounter;

        public ResizablePoolCell(ActorSystem system, InternalActorRef supervisor, Props routerProps,
            Props routeeProps, ActorPath path, Mailbox mailbox, Pool pool)
            : base(system, supervisor, routerProps, routeeProps, path, mailbox)
        {
            if (pool.Resizer == null)
                throw new ArgumentException("RouterConfig must be a Pool with defined resizer");

            resizer = pool.Resizer;
        }

        
    }
}
