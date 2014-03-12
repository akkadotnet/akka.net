using Akka.Actor;
using Akka.Dispatch.SysMsg;

namespace Akka.Routing
{
    public class RoutedActorRef : LocalActorRef
    {
        private readonly Router router;
        private readonly RoutedActorCell cell;
        public RoutedActorRef( ActorPath path, RoutedActorCell context) : base(path, context)
        {
            this.cell = context;
        }

        protected override void TellInternal(object message, ActorRef sender)
        {
            if (message is SystemMessage)
            {
                Cell.Post(sender,message);
            }
            else
            {
                cell.Router.Route(message, sender);    
            }            
        }
    }
}