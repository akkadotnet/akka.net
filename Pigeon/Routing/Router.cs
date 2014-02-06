using Pigeon.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Routing
{
    internal class NoRoutee : Routee
    {
        public override void Send(object message, ActorRef sender)
        {
            if (sender is LocalActorRef)
            {
                sender.AsInstanceOf<LocalActorRef>().Provider.DeadLetters.Tell(message);
            }
        }
    }

    public class Routee
    {
        public static readonly Routee NoRoutee = new NoRoutee();

        public virtual void Send(object message,ActorRef sender)
        {
           
        }
    }

    public class RouterEnvelope
    {
        public RouterEnvelope(object message)
        {
            this.Message = message;
        }

        public object Message { get;private set; }
    }
    
    public class RoutingLogic
    {

    }
    public class Router
    {
        public Router(RoutingLogic logic, params Routee[] routees)
        {
            if (routees == null)
            {
                routees = new Routee[0];
            }
        }

        private object UnWrap(object message)
        {
            if (message is RouterEnvelope)
            {
                return message.AsInstanceOf<RouterEnvelope>().Message;
            }

            return message;
        }

        public virtual void Send(Routee routee,object message,ActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }
    }
}
