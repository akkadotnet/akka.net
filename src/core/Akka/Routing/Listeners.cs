﻿//-----------------------------------------------------------------------
// <copyright file="Listeners.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Routing
{
    /// <summary>
    /// IListeners is a generic interface to implement listening capabilities on an Actor.
    /// 
    /// <remarks>Use the <see cref="ListenerSupport.Gossip(object)"/> method to send a message to the listeners</remarks>.
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
        public Listen(IActorRef listener)
        {
            Listener = listener;
        }

        public IActorRef Listener { get; private set; }
    }

    public class Deafen : ListenerMessage
    {
        public Deafen(IActorRef listener)
        {
            Listener = listener;
        }

        public IActorRef Listener { get; private set; }
    }

    public class WithListeners : ListenerMessage
    {
        public WithListeners(Action<IActorRef> listenerFunction)
        {
            ListenerFunction = listenerFunction;
        }

        public Action<IActorRef> ListenerFunction { get; private set; }
    }

    /// <summary>
    /// Adds <see cref="IListeners"/> capabilities to a class, but has to be wired int manually into
    /// the <see cref="ActorBase.OnReceive"/> method.
    /// </summary>
    public class ListenerSupport
    {
        protected readonly HashSet<IActorRef> Listeners = new HashSet<IActorRef>();

        /// <summary>
        /// Chain this into the <see cref="ActorBase.OnReceive"/> function.
        /// </summary>
        public Receive ListenerReceive
        {
            get{ return message =>
            {
                var match=PatternMatch.Match(message)
                    .With<Listen>(m => Add(m.Listener))
                    .With<Deafen>(d => Remove(d.Listener))
                    .With<WithListeners>(f =>
                    {
                        foreach (var listener in Listeners)
                        {
                            f.ListenerFunction(listener);
                        }
                    });
                return match.WasHandled;
            };}
        }

        public void Add(IActorRef actor)
        {
            if(!Listeners.Contains(actor))
                Listeners.Add(actor);
        }

        public void Remove(IActorRef actor)
        {
            if(Listeners.Contains(actor))
                Listeners.Remove(actor);
        }

        /// <summary>
        /// Send the supplied message to all listeners
        /// </summary>
        public void Gossip(object msg)
        {
            Gossip(msg, ActorRefs.NoSender);
        }

        /// <summary>
        /// Send the supplied message to all listeners
        /// </summary>
        public void Gossip(object msg, IActorRef sender)
        {
            foreach(var listener in Listeners)
                listener.Tell(msg, sender);
        }
    }
}

