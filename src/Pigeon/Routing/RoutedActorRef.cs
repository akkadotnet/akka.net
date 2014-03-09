using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Routing
{
    public class RoutedActorRef : LocalActorRef
    {
        private readonly Router router;

        public RoutedActorRef(Router router, ActorPath path, ActorCell context) : base(path, context)
        {
            this.router = router;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message is SystemMessage)
            {
                Cell.Post(sender,message);
            }
            else
            {
                router.Route(message, sender);    
            }            
        }
    }
}