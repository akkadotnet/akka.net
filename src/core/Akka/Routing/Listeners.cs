//-----------------------------------------------------------------------
// <copyright file="Listeners.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// This interface is needed to implement listening capabilities on an actor.
    /// 
    /// <remarks>
    /// <ul>
    /// <li>Use the <see cref="ListenerSupport.Gossip(object)"/> method to send a message to the listeners.</li>
    /// <li>Send <code>Listen(Self)</code> to another Actor to start listening.</li>
    /// <li>Send <code>Deafen(Self)</code> to another Actor to stop listening.</li>
    /// <li>Send <code>WithListeners(delegate)</code> to traverse the current listeners.</li>
    /// </ul>
    /// </remarks>
    /// </summary>
    public interface IListeners
    {
        /// <summary>
        /// Retrieves the support needed to interact with an actor's listeners.
        /// </summary>
        ListenerSupport Listeners { get; }
    }

    /// <summary>
    /// This class represents a message sent by an actor to another actor that is listening to it.
    /// </summary>
    public abstract class ListenerMessage {}

    /// <summary>
    /// The class represents a <see cref="ListenerMessage"/> sent by an <see cref="IActorRef"/> to another <see cref="IActorRef"/>
    /// instructing the second actor to start listening for messages sent by the first actor.
    /// </summary>
    public sealed class Listen : ListenerMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Listen"/> class.
        /// </summary>
        /// <param name="listener">The actor that receives the message.</param>
        public Listen(IActorRef listener)
        {
            Listener = listener;
        }

        /// <summary>
        /// The actor that receives the message.
        /// </summary>
        public IActorRef Listener { get; }
    }

    /// <summary>
    /// The class represents a <see cref="ListenerMessage"/> sent by an <see cref="IActorRef"/> to another <see cref="IActorRef"/>
    /// instructing the second actor to stop listening for messages sent by the first actor.
    /// </summary>
    public sealed class Deafen : ListenerMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Deafen"/> class.
        /// </summary>
        /// <param name="listener">The actor that no longer receives the message.</param>
        public Deafen(IActorRef listener)
        {
            Listener = listener;
        }

        /// <summary>
        /// The actor that no longer receives the message.
        /// </summary>
        public IActorRef Listener { get; }
    }

    /// <summary>
    /// This class represents a <see cref="ListenerMessage"/> instructing an <see cref="IActorRef"/>
    /// to perform a supplied <see cref="Action{IActorRef}"/> for all of its listeners.
    /// </summary>
    public sealed class WithListeners : ListenerMessage
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="WithListeners"/> class.
        /// </summary>
        /// <param name="listenerFunction">The action to perform for all of an actor's listeners.</param>
        public WithListeners(Action<IActorRef> listenerFunction)
        {
            ListenerFunction = listenerFunction;
        }

        /// <summary>
        /// The action to perform for all of an actor's listeners.
        /// </summary>
        public Action<IActorRef> ListenerFunction { get; }
    }

    /// <summary>
    /// This class adds <see cref="IListeners"/> capabilities to an actor.
    /// 
    /// <note>
    /// <see cref="ListenerReceive"/> must be wired manually into the actor's
    /// <see cref="UntypedActor.OnReceive"/> method.
    /// </note>
    /// </summary>
    public class ListenerSupport
    {
        /// <summary>
        /// The collection of registered listeners that is listening for messages from an actor.
        /// </summary>
        protected readonly HashSet<IActorRef> Listeners = new HashSet<IActorRef>();

        /// <summary>
        /// Retrieves the wiring needed to implement listening functionality.
        /// 
        /// <note>
        /// This needs to be chained into the actor's <see cref="UntypedActor.OnReceive"/> method.
        /// </note>
        /// </summary>
        public Receive ListenerReceive
        {
            get
            {
                return message =>
                {
                    switch (message)
                    {
                        case Listen listen:
                            Add(listen.Listener);
                            return true;
                        case Deafen deafen:
                            Remove(deafen.Listener);
                            return true;
                        case WithListeners listeners:
                            foreach (var listener in Listeners)
                                listeners.ListenerFunction(listener);
                            return true;
                        default:
                            return false;
                    }
                };
            }
        }

        /// <summary>
        /// Adds the specified actor to the collection of registered listeners.
        /// </summary>
        /// <param name="actor">The actor to add to the collection of registered listeners.</param>
        public void Add(IActorRef actor)
        {
            if(!Listeners.Contains(actor))
                Listeners.Add(actor);
        }

        /// <summary>
        /// Removes the specified actor from the collection of registered listeners.
        /// </summary>
        /// <param name="actor">The actor to remove from the collection of registered listeners.</param>
        public void Remove(IActorRef actor)
        {
            if(Listeners.Contains(actor))
                Listeners.Remove(actor);
        }

        /// <summary>
        /// Sends the supplied message to all registered listeners.
        /// 
        /// <note>
        /// Messages sent this way use <see cref="ActorRefs.NoSender"/> as the sender.
        /// </note>
        /// </summary>
        /// <param name="message">The message sent to all registered listeners.</param>
        public void Gossip(object message)
        {
            Gossip(message, ActorRefs.NoSender);
        }

        /// <summary>
        /// Sends the supplied message to all registered listeners.
        /// </summary>
        /// <param name="message">The message sent to all registered listeners.</param>
        /// <param name="sender">The actor that sends the message.</param>
        public void Gossip(object message, IActorRef sender)
        {
            foreach(var listener in Listeners)
                listener.Tell(message, sender);
        }
    }
}
