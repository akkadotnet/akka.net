using System.Collections.Generic;
using Akka.Actor;
using System;
using System.Threading.Tasks;
using System.Linq;

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

        public virtual Task Ask(object message, TimeSpan? timeout)
        {
            return null;
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

        public override Task Ask(object message, TimeSpan? timeout)
        {
            return Actor.Ask(message, timeout);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorRefRoutee)obj);
        }

        protected bool Equals(ActorRefRoutee other)
        {
            return Equals(Actor, other.Actor);
        }

        public override int GetHashCode()
        {
            return (Actor != null ? Actor.GetHashCode() : 0);
        }
    }

    public class ActorSelectionRoutee : Routee
    {
        private readonly ActorSelection _actor;

        public ActorSelection Selection { get { return _actor; } }

        public ActorSelectionRoutee(ActorSelection actor)
        {
            _actor = actor;
        }

        public override void Send(object message, ActorRef sender)
        {
            _actor.Tell(message, sender);
        }

        public override Task Ask(object message, TimeSpan? timeout)
        {
            return _actor.Ask(message, timeout);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorSelectionRoutee)obj);
        }

        protected bool Equals(ActorSelectionRoutee other)
        {
            return Equals(_actor, other._actor);
        }

        public override int GetHashCode()
        {
            return (_actor != null ? _actor.GetHashCode() : 0);
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
            return new Router(logic, Include(routee));
        }

        public Router AddRoutee(ActorRef routee)
        {
            return new Router(logic, Include(new ActorRefRoutee(routee)));
        }

        public Router AddRoutee(ActorSelection routee)
        {
            return new Router(logic, Include(new ActorSelectionRoutee(routee)));
        }

        private Routee[] Include(Routee routee)
        {
            return routees.Union(Enumerable.Repeat(routee, 1)).ToArray();
        }
    }
}