using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Routing
{
    public class RoutedActorRef : LocalActorRef
    {
        private Router router;
        public RoutedActorRef(Router router, ActorPath path,ActorCell context) : base(path,context)
        {
            this.router = router;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            router.Route(message, sender);
        }
    }
}
