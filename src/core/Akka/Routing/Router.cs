//-----------------------------------------------------------------------
// <copyright file="Router.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using System;
using System.Threading.Tasks;
using System.Linq;
using Akka.Util.Internal;

namespace Akka.Routing
{
    internal class NoRoutee : Routee
    {
        public override void Send(object message, IActorRef sender)
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

        public virtual void Send(object message, IActorRef sender)
        {
        }

        public virtual Task Ask(object message, TimeSpan? timeout)
        {
            return null;
        }


        public static Routee FromActorRef(IActorRef actorRef)
        {
            return new ActorRefRoutee(actorRef);
        }
    }

    public class ActorRefRoutee : Routee
    {
        public IActorRef Actor { get; private set; }

        public ActorRefRoutee(IActorRef actor)
        {
            this.Actor = actor;
        }

        public override void Send(object message, IActorRef sender)
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

        public override void Send(object message, IActorRef sender)
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

        public override void Send(object message, IActorRef sender)
        {
            foreach (Routee routee in  routees)
            {
                routee.Send(message, sender);
            }
        }
    }

    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route messages to one or more actors.
    /// These actors are known in the system as a <see cref="Routee"/>.
    /// </summary>
    public abstract class RoutingLogic
    {
        /// <summary>
        /// Picks a <see cref="Routee"/> to receive the <paramref name="message"/>.
        /// <note>
        /// Normally it picks one of the passed routees, but it is up to the implementation
        /// to return whatever <see cref="Routee"/> to use for sending a specific message.
        /// </note>
        /// </summary>
        /// <param name="message">The message that is being routed</param>
        /// <param name="routees">A collection of routees to choose from when receiving the <paramref name="message"/>.</param>
        /// <returns>A <see cref="Routee"/> that receives the <paramref name="message"/>.</returns>
        public abstract Routee Select(object message, Routee[] routees);
    }

    public class Router
    {
        private readonly RoutingLogic _logic;
        private readonly Routee[] _routees;

        //The signature might look funky. Why not just Router(RoutingLogic logic, params ActorRef[] routees) ? 
        //We need one unique constructor to handle this call: new Router(logic). The other constructor will handle that.
        //So in order to not confuse the compiler we demand at least one ActorRef. /@hcanber
        public Router(RoutingLogic logic, IActorRef routee, params IActorRef[] routees)
        {
            var routeesLength = routees.Length;
            if (routees == null || routeesLength == 0)
            {
                _routees = new[] { Routee.FromActorRef(routee) };
            }
            else
            {
                //Convert and put routee first in a new array
                var rts = new Routee[routeesLength + 1];
                rts[0] = Routee.FromActorRef(routee);

                //Convert all routees and put them into the new array
                for (var i = 0; i < routeesLength; i++)
                {
                    var actorRef = routees[i];
                    var r = Routee.FromActorRef(actorRef);
                    rts[i + 1] = r;
                }
                _routees = rts;
            }
            _logic = logic;
        }


        public Router(RoutingLogic logic, params Routee[] routees)
        {
            if(routees == null)
            {
                routees = new Routee[0];
            }
            _routees = routees;
            _logic = logic;
        }
        public IEnumerable<Routee> Routees
        {
            get { return _routees; }
        }

        public RoutingLogic RoutingLogic
        {
            get { return _logic; }
        }


        private object UnWrap(object message)
        {
            if (message is RouterEnvelope)
            {
                return message.AsInstanceOf<RouterEnvelope>().Message;
            }

            return message;
        }

        public void Route(object message, IActorRef sender)
        {
            if (message is Broadcast)
            {
                new SeveralRoutees(_routees).Send(UnWrap(message), sender);
            }
            else
            {
                Send(_logic.Select(message, _routees), message, sender);
            }
        }

        protected virtual void Send(Routee routee, object message, IActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }

        /// <summary>
        /// Create a new instance with the specified routees and the same <see cref="RoutingLogic"/>.
        /// </summary>
        public Router WithRoutees(params Routee[] routees)
        {
            return new Router(_logic, routees);
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>
        public Router AddRoutee(Routee routee)
        {
            return new Router(_logic, _routees.Union(new[]{routee}).ToArray());
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>
        public Router AddRoutee(IActorRef routee)
        {
            return AddRoutee(new ActorRefRoutee(routee));
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>  
        public Router AddRoutee(ActorSelection routee)
        {
           return AddRoutee(new ActorSelectionRoutee(routee));
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        public Router RemoveRoutee(Routee routee)
        {
            var routees = _routees.Where(r => !r.Equals(routee)).ToArray();
            return new Router(_logic, routees);
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        public Router RemoveRoutee(IActorRef routee)
        {
            return RemoveRoutee(new ActorRefRoutee(routee));
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        public Router RemoveRoutee(ActorSelection routee)
        {
            return RemoveRoutee(new ActorSelectionRoutee(routee));
        }
    }
}

