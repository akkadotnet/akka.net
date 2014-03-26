using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// IListeners is a genric interface to implement listening capabilities on an Actor.
    /// 
    /// <remarks>Use the <see cref="ListenerSupport.Gossip"/> method to send a message to the listeners</remarks>.
    /// <remarks>Send <code>Listen(Self)</code> to another Actor to start listening.</remarks>
    /// <remarks>Send <code>Deafen(Self)</code> to another Actor to stop listening.</remarks>
    /// <remarks>Send <code>WithListeners(delegate)</code> to traverse the current listeners.</remarks>
    /// </summary>
    public interface IListeners
    {
        ListenerSupport Listeners { get; }
    }

    public abstract class ListenerMessage {}

    public class Listen : ListenerMessage
    {
        public Listen(ActorRef listener)
        {
            Listener = listener;
        }

        public ActorRef Listener { get; private set; }
    }

    public class Deafen : ListenerMessage
    {
        public Deafen(ActorRef listener)
        {
            Listener = listener;
        }

        public ActorRef Listener { get; private set; }
    }

    public class WithListeners : ListenerMessage
    {
        public WithListeners(Action<ActorRef> listenerFunction)
        {
            ListenerFunction = listenerFunction;
        }

        public Action<ActorRef> ListenerFunction { get; private set; }
    }

    /// <summary>
    /// Adds <see cref="IListeners"/> capabilities to a class, but has to be wired int manually into
    /// the <see cref="ActorBase.OnReceive"/> method.
    /// </summary>
    public class ListenerSupport
    {
        protected HashSet<ActorRef> Listeners = new HashSet<ActorRef>();

        /// <summary>
        /// Chain this into the <see cref="ActorBase.OnReceive"/> function.
        /// </summary>
        public Receive ListenerReceive
        {
            get{ return message =>
            {
                PatternMatch.Match(message)
                    .With<Listen>(m => Listeners.Add(m.Listener))
                    .With<Deafen>(d => Listeners.Remove(d.Listener))
                    .With<WithListeners>(f =>
                    {
                        foreach (var listener in Listeners)
                        {
                            f.ListenerFunction(listener);
                        }
                    });
            };}
        }

        /// <summary>
        /// Send the supplied message to all listeners
        /// </summary>
        public void Gossip(object msg)
        {
            Gossip(msg, ActorRef.NoSender);
        }

        /// <summary>
        /// Send the supplied message to all listeners
        /// </summary>
        public void Gossip(object msg, ActorRef sender)
        {
            foreach(var listener in Listeners)
                listener.Tell(msg, sender);
        }
    }
}
