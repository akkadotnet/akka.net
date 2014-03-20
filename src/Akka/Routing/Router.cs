﻿using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Routing
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

        public virtual void Send(object message, ActorRef sender)
        {
        }
    }

    public class ActorRefRoutee : Routee
    {
        public ActorRef Actor { get; private set; }

        public ActorRefRoutee(ActorRef actor)
        {
            this.Actor = actor;
        }

        public override void Send(object message, ActorRef sender)
        {
            Actor.Tell(message, sender);
        }
    }

    public class ActorSelectionRoutee : Routee
    {
        private readonly ActorSelection actor;

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
            Message = message;
        }

        public object Message { get; private set; }
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
        private readonly Routee[] routees;

        public SeveralRoutees(Routee[] routees)
        {
            this.routees = routees;
        }

        public override void Send(object message, ActorRef sender)
        {
            foreach (Routee routee in  routees)
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
        private readonly RoutingLogic logic;
        private readonly Routee[] routees;

        public Router(RoutingLogic logic, params Routee[] routees)
        {
            if (routees == null)
            {
                routees = new Routee[0];
            }
            this.routees = routees;
            this.logic = logic;
        }

        public IEnumerable<Routee> Routees
        {
            get { return routees; }
        }

        private object UnWrap(object message)
        {
            if (message is RouterEnvelope)
            {
                return message.AsInstanceOf<RouterEnvelope>().Message;
            }

            return message;
        }

        public void Route(object message, ActorRef sender)
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

        public virtual void Send(Routee routee, object message, ActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }

        public Router WithRoutees(params Routee[] routees)
        {
            return new Router(logic, routees);
        }

        public Router AddRoutee(Routee routee)
        {
            return new Router(logic, routees.Add(routee));
        }

        public Router AddRoutee(ActorRef routee)
        {
            return new Router(logic, routees.Add(new ActorRefRoutee(routee)));
        }

        public Router AddRoutee(ActorSelection routee)
        {
            return new Router(logic, routees.Add(new ActorSelectionRoutee(routee)));
        }
    }
}