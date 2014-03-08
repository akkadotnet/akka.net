using Akka.Actor;

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
            router.Route(message, sender);
        }
    }
}