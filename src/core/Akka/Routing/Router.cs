//-----------------------------------------------------------------------
// <copyright file="Router.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using System.Linq;
using Akka.Annotations;
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
    /// A <see cref="Routee"/> that resolves to an <see cref="IActorRef"/>
    /// </summary>
    public class ActorRefRoutee : Routee
    {
        /// <summary>
        /// The <see cref="IActorRef"/> this routee sends messages to.
        /// </summary>
        public IActorRef Actor { get; }
        
        public ActorRefRoutee(IActorRef actor)
        {
            Actor = actor;
        }

        /// <inheritdoc cref="Routee.Send"/>
        public override void Send(object message, IActorRef sender)
        {
            Actor.Tell(message, sender);
        }

        /// <inheritdoc cref="Routee.Ask"/>
        public override Task<object> Ask(object message, TimeSpan? timeout)
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

        /// <inheritdoc/>
        protected bool Equals(ActorRefRoutee other) => Equals(Actor, other.Actor);

        
        public override int GetHashCode() => Actor?.GetHashCode() ?? 0;
    }

    
    /// <summary>
    /// A <see cref="Routee"/> that resolves to an <see cref="ActorSelection"/>
    /// </summary>
    public class ActorSelectionRoutee : Routee
    {
        /// <summary>
        /// The <see cref="ActorSelection"/> this routee sends to.
        /// </summary>
        public ActorSelection Selection { get; }
        
        public ActorSelectionRoutee(ActorSelection actor)
        {
            Selection = actor;
        }

        /// <inheritdoc cref="Routee.Send"/>
        public override void Send(object message, IActorRef sender)
        {
            Selection.Tell(message, sender);
        }

        /// <inheritdoc cref="Routee.Ask"/>
        public override Task<object> Ask(object message, TimeSpan? timeout)
        {
            return Selection.Ask(message, timeout);
        }

       
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((ActorSelectionRoutee)obj);
        }

        /// <inheritdoc/>
        protected bool Equals(ActorSelectionRoutee other) => Equals(Selection, other.Selection);

        
        public override int GetHashCode() => Selection?.GetHashCode() ?? 0;
    }

    /// <summary>
    /// Used for some types of messages to separate router management messages
    /// from the actual content of messages sent to routees.
    /// </summary>
    public class RouterEnvelope
    {
        public RouterEnvelope(object message)
        {
            Message = message;
        }

        /// <summary>
        /// The management message
        /// </summary>
        public object Message { get; }
    }

    /// <summary>
    /// A special <see cref="RouterEnvelope"/> that will broadcast a message from this router
    /// to all <see cref="Routee"/>s regardless of the <see cref="RoutingLogic"/>.
    /// </summary>
    public class Broadcast : RouterEnvelope
    {
        public Broadcast(object message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// INTERNAL API
    /// </summary>
    public class SeveralRoutees : Routee
    {
        private readonly Routee[] _routees;
        
        public SeveralRoutees(Routee[] routees)
        {
            _routees = routees;
        }

        /// <inheritdoc cref="Routee.Send"/>
        public override void Send(object message, IActorRef sender)
        {
            foreach (var routee in _routees)
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
    /// Implements a router - including:
    /// 
    ///  - The <see cref="RoutingLogic"/>
    ///  - The <see cref="Routees"/>
    ///
    /// It's used internally by the routing system
    /// 
    /// </summary>
    public class Router
    {
        private readonly Routee[] _routees;

        //The signature might look funky. Why not just Router(RoutingLogic logic, params ActorRef[] routees) ? 
        //We need one unique constructor to handle this call: new Router(logic). The other constructor will handle that.
        //So in order to not confuse the compiler we demand at least one ActorRef. /@hcanber
        /// <summary>
        /// INTERNAL API
        ///
        /// For testing purposes only.
        /// </summary>
        /// <param name="logic">TBD</param>
        /// <param name="routee">TBD</param>
        /// <param name="routees">TBD</param>
        [InternalApi]
        public Router(RoutingLogic logic, IActorRef routee, params IActorRef[] routees)
        {
            if (routees == null || routees.Length == 0)
            {
                _routees = new []{ Routee.FromActorRef(routee) };
            }
            else
            {
                var routeesLength = routees.Length;

                //Convert and put routee first in a new array
                _routees = new Routee[routeesLength+1];
                _routees[0] = Routee.FromActorRef(routee);

                //Convert all routees and put them into the new array
                for (var i = 0; i < routees.Length; i++)
                {
                    var actorRef = routees[i];
                    var r = Routee.FromActorRef(actorRef);
                    _routees[i + 1] = r;
                }
            }
            RoutingLogic = logic;
        }


        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="logic">TBD</param>
        /// <param name="routees">TBD</param>
        public Router(RoutingLogic logic, params Routee[] routees)
        {
            _routees = routees ?? Array.Empty<Routee>();
            RoutingLogic = logic;
        }
        
        /// <summary>
        /// The set of <see cref="Routees"/> that this router is currently targeting.
        /// </summary>
        public IEnumerable<Routee> Routees
        {
            get { return _routees; }
        }

        /// <summary>
        /// The logic used to determine which <see cref="Routee"/> will process a given message.
        /// </summary>
        public RoutingLogic RoutingLogic { get; }

        protected object UnWrap(object message)
        {
            if (message is RouterEnvelope routerEnvelope)
            {
                return routerEnvelope.Message;
            }

            return message;
        }

        /// <summary>
        /// Uses the <see cref="RoutingLogic"/> to select an appropriate routee (or routees)
        /// and then delivers the message to them via <see cref="ICanTell.Tell"/>.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender of this message - which will be propagated to the routee.</param>
        public virtual void Route(object message, IActorRef sender)
        {
            if (message is Broadcast)
            {
                var unwrapped = UnWrap(message);
                foreach (var r in _routees)
                {
                    r.Send(unwrapped, sender);
                }
            }
            else
            {
                Send(RoutingLogic.Select(message, _routees), message, sender);
            }
        }

        /// <summary>
        /// Sends the message to the routee from the sender - and unwraps any special <see cref="RouterEnvelope"/>S
        /// first so the routee only receives the intended message.
        /// </summary>
        /// <param name="routee">The routee who has been selected by the <see cref="RoutingLogic"/>.</param>
        /// <param name="message">The message to send.</param>
        /// <param name="sender">The sender of this message - which will be propagated to the routee.</param>
        protected virtual void Send(Routee routee, object message, IActorRef sender)
        {
            routee.Send(UnWrap(message), sender);
        }

        /// <summary>
        /// Create a new instance with the specified routees and the same <see cref="RoutingLogic"/>.
        /// </summary>
        /// <param name="routees">The routees to add.</param>
        /// <returns>A new <see cref="Router"/> instance with these routees added.</returns>
        public virtual Router WithRoutees(params Routee[] routees)
        {
            return new Router(RoutingLogic, routees);
        }

        /// <summary>
        /// Create a new instance with one more routee and the same <see cref="RoutingLogic"/>.
        /// </summary>
        /// <param name="routee">The routee to add.</param>
        /// <returns>A new <see cref="Router"/> instance with this routee added.</returns>
        public virtual Router AddRoutee(Routee routee)
        {
            return new Router(RoutingLogic, _routees.Union(new[]{routee}).ToArray());
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
        /// <param name="routee">The routee to remove.</param>
        /// <returns>A new <see cref="Router"/> instance with this same configuration, sans routee.</returns>
        public virtual Router RemoveRoutee(Routee routee)
        {
            var routees = _routees.Where(r => !r.Equals(routee)).ToArray();
            return new Router(RoutingLogic, routees);
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        /// <param name="routee">The <see cref="IActorRef"/> routee to remove.</param>
        /// <returns>A new <see cref="Router"/> instance with this same configuration, sans routee.</returns>
        public Router RemoveRoutee(IActorRef routee)
        {
            return RemoveRoutee(new ActorRefRoutee(routee));
        }

        /// <summary>
        /// Create a new instance without the specified routee.
        /// </summary>
        /// <param name="routee">The <see cref="ActorSelection"/> routee to remove.</param>
        /// <returns>A new <see cref="Router"/> instance with this same configuration, sans routee.</returns>
        public Router RemoveRoutee(ActorSelection routee)
        {
            return RemoveRoutee(new ActorSelectionRoutee(routee));
        }
    }
}

