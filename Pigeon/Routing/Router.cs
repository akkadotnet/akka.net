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

    public class ActorRefRoutee : Routee
    {
        private ActorRef actor;
        public ActorRefRoutee (ActorRef actor)
        {
            this.actor = actor;
        }

        public override void Send(object message, ActorRef sender)
        {
            actor.Tell(message, sender);
        }
    }

    public class ActorSelectionRoutee : Routee
    {
        private ActorSelection actor;
        public ActorSelectionRoutee(ActorSelection actor)
        {
            this.actor = actor;
        }

        public override void Send(object message, ActorRef sender)
        {
            actor.Tell(message, sender);
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

    public class Broadcast : RouterEnvelope
    {
        public Broadcast(object message)
            : base(message)
        {

        }
    }

    public class SeveralRoutees : Routee
    {
        private Routee[] routees;
        public SeveralRoutees(Routee[] routees)
        {
            this.routees = routees;
        }

        public override void Send(object message, ActorRef sender)
        {
            foreach(var routee in  routees)
            {
                routee.Send(message, sender);
            }
        }
    }
    
    public abstract class RoutingLogic
    {

        public abstract Routee Select(object message, Routee[] routees);        
    }

    public class Router
    {
        private Routee[] routees;
        private RoutingLogic logic;
        public Router(RoutingLogic logic, params Routee[] routees)
        {
            if (routees == null)
            {
                routees = new Routee[0];
            }
            this.routees = routees;
            this.logic = logic;
        }

        private object UnWrap(object message)
        {
            if (message is RouterEnvelope)
            {
                return message.AsInstanceOf<RouterEnvelope>().Message;
            }

            return message;
        }

        public void Route(object message,ActorRef sender)
        {
            if (message is Broadcast)
            {
                new SeveralRoutees(routees).Send(UnWrap(message), sender);
            }
            else
            {
                Send(logic.Select(message, routees), message, sender);
            }
        }

        public virtual void Send(Routee routee,object message,ActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }     
   
        public Router WithRoutees(params Routee[] routees)
        {
            return new Router(logic, routees);
        }

        public Router AddRoutee(Routee routee)
        {
            return new Router(logic, this.routees.Add(routee));
        }

        public Router AddRoutee(ActorRef routee)
        {
            return new Router(logic, this.routees.Add(new ActorRefRoutee(routee)));
        }

        public Router AddRoutee(ActorSelection routee)
        {
            return new Router(logic, this.routees.Add(new ActorSelectionRoutee(routee)));
        }
    }
}
