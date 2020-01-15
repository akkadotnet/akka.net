//-----------------------------------------------------------------------
// <copyright file="Router.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
    /// <summary>
    /// INTERNAL API
    /// 
    /// Used to describe instance where no routee is available.
    /// </summary>
    internal class NoRoutee : Routee
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public override void Send(object message, IActorRef sender)
        {
            if (sender is LocalActorRef localActorRef)
            {
                localActorRef.Provider.DeadLetters.Tell(message);
            }
        }
    }

    /// <summary>
    /// Generic base class for routees.
    /// </summary>
    public class Routee
    {
        /// <summary>
        /// Singleton instance for special case "no routee," for when no
        /// matching routees are found.
        /// </summary>
        public static readonly Routee NoRoutee = new NoRoutee();

        /// <summary>
        /// Send a message to the routee.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender, if any.</param>
        public virtual void Send(object message, IActorRef sender)
        {
        }

        /// <summary>
        /// Ask a routee for a reply message in response to an input.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="timeout">Optional timeout parameter. If the parameter is provided
        /// and the operation times out, will throw an AskTimeoutException.</param>
        /// <returns>A Task containing the response object.</returns>
        public virtual Task<object> Ask(object message, TimeSpan? timeout)
        {
            return null;
        }


        /// <summary>
        /// Helper method to create a new Routee instance from an IActorRef.
        /// </summary>
        public static Routee FromActorRef(IActorRef actorRef)
        {
            return new ActorRefRoutee(actorRef);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ActorRefRoutee : Routee
    {
        /// <summary>
        /// TBD
        /// </summary>
        public IActorRef Actor { get; private set; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        public ActorRefRoutee(IActorRef actor)
        {
            this.Actor = actor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public override void Send(object message, IActorRef sender)
        {
            Actor.Tell(message, sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public override Task<object> Ask(object message, TimeSpan? timeout)
        {
            return Actor.Ask(message, timeout);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorRefRoutee)obj);
        }

        /// <inheritdoc/>
        protected bool Equals(ActorRefRoutee other) => Equals(Actor, other.Actor);

        /// <inheritdoc/>
        public override int GetHashCode() => Actor?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class ActorSelectionRoutee : Routee
    {
        private readonly ActorSelection _actor;

        /// <summary>
        /// TBD
        /// </summary>
        public ActorSelection Selection { get { return _actor; } }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="actor">TBD</param>
        public ActorSelectionRoutee(ActorSelection actor)
        {
            _actor = actor;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public override void Send(object message, IActorRef sender)
        {
            _actor.Tell(message, sender);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <returns>TBD</returns>
        public override Task<object> Ask(object message, TimeSpan? timeout)
        {
            return _actor.Ask(message, timeout);
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorSelectionRoutee)obj);
        }

        /// <inheritdoc/>
        protected bool Equals(ActorSelectionRoutee other) => Equals(_actor, other._actor);

        /// <inheritdoc/>
        public override int GetHashCode() => _actor?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class RouterEnvelope
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public RouterEnvelope(object message)
        {
            Message = message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public object Message { get; private set; }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class Broadcast : RouterEnvelope
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        public Broadcast(object message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SeveralRoutees : Routee
    {
        private readonly Routee[] routees;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routees">TBD</param>
        public SeveralRoutees(Routee[] routees)
        {
            this.routees = routees;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        public override void Send(object message, IActorRef sender)
        {
            foreach (Routee routee in routees)
            {
                routee.Send(message, sender);
            }
        }
    }

    /// <summary>
    /// This class contains logic used by a <see cref="Router"/> to route messages to one or more actors.
    /// These actors are known in the system as a <see cref="Routee"/>.
    /// </summary>
    public abstract class RoutingLogic : INoSerializationVerificationNeeded
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

    /// <summary>
    /// TBD
    /// </summary>
    public class Router
    {
        private readonly RoutingLogic _logic;
        private readonly Routee[] _routees;

        //The signature might look funky. Why not just Router(RoutingLogic logic, params ActorRef[] routees) ? 
        //We need one unique constructor to handle this call: new Router(logic). The other constructor will handle that.
        //So in order to not confuse the compiler we demand at least one ActorRef. /@hcanber
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logic">TBD</param>
        /// <param name="routee">TBD</param>
        /// <param name="routees">TBD</param>
        public Router(RoutingLogic logic, IActorRef routee, params IActorRef[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                _routees = new[] { Routee.FromActorRef(routee) };
            }
            else
            {
                var routeesLength = routees.Length;

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


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logic">TBD</param>
        /// <param name="routees">TBD</param>
        public Router(RoutingLogic logic, params Routee[] routees)
        {
            _routees = routees ?? new Routee[0];
            _logic = logic;
        }
        /// <summary>
        /// TBD
        /// </summary>
        public IEnumerable<Routee> Routees
        {
            get { return _routees; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        public RoutingLogic RoutingLogic
        {
            get { return _logic; }
        }


        private object UnWrap(object message)
        {
            if (message is RouterEnvelope routerEnvelope)
            {
                return routerEnvelope.Message;
            }

            return message;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <param name="message">TBD</param>
        /// <param name="sender">TBD</param>
        protected virtual void Send(Routee routee, object message, IActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }

        /// <summary>
        /// Create a new instance with the specified routees and the same <see cref="RoutingLogic"/>.
        /// </summary>
        /// <param name="routees">TBD</param>
        /// <returns>TBD</returns>
        public Router WithRoutees(params Routee[] routees)
        {
            return new Router(_logic, routees);
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router AddRoutee(Routee routee)
        {
            return new Router(_logic, _routees.Union(new[]{routee}).ToArray());
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router AddRoutee(IActorRef routee)
        {
            return AddRoutee(new ActorRefRoutee(routee));
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>  
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router AddRoutee(ActorSelection routee)
        {
           return AddRoutee(new ActorSelectionRoutee(routee));
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router RemoveRoutee(Routee routee)
        {
            var routees = _routees.Where(r => !r.Equals(routee)).ToArray();
            return new Router(_logic, routees);
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router RemoveRoutee(IActorRef routee)
        {
            return RemoveRoutee(new ActorRefRoutee(routee));
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        /// <param name="routee">TBD</param>
        /// <returns>TBD</returns>
        public Router RemoveRoutee(ActorSelection routee)
        {
            return RemoveRoutee(new ActorSelectionRoutee(routee));
        }
    }
}

