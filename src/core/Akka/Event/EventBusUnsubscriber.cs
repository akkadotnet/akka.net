//-----------------------------------------------------------------------
// <copyright file="EventBusUnsubscribers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Util.Internal;

namespace Akka.Event
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Watches all actors which subscribe on the given eventStream, and unsubscribes them from it when they are Terminated.
    /// 
    /// Assumptions note:
    ///  We do not guarantee happens-before in the EventStream when 2 threads subscribe(a) / unsubscribe(a) on the same actor,
    /// thus the messages sent to this actor may appear to be reordered - this is fine, because the worst-case is starting to
    /// needlessly watch the actor which will not cause trouble for the stream. This is a trade-off between slowing down
    /// subscribe calls * because of the need of linearizing the history message sequence and the possibility of sometimes
    /// watching a few actors too much - we opt for the 2nd choice here.
    /// </summary>
    class EventStreamUnsubscriber : ActorBase
    {
        private readonly EventStream _eventStream;
        private readonly bool _debug;
        private readonly ActorSystem _system;

        public EventStreamUnsubscriber(EventStream eventStream, ActorSystem system, bool debug)
        {
            _eventStream = eventStream;
            _system = system;
            _debug = debug;
           
        }

        protected override bool Receive(object message)
        {
            return message.Match().With<Register>(register =>
            {
                if (_debug)
                    _eventStream.Publish(new Debug(this.GetType().Name, GetType(),
                       string.Format("watching {0} in order to unsubscribe from EventStream when it terminates", register.Actor)));
                Context.Watch(register.Actor);
            }).With<UnregisterIfNoMoreSubscribedChannels>(unregister =>
            {
                if (_debug)
                    _eventStream.Publish(new Debug(this.GetType().Name, GetType(),
                        string.Format("unwatching {0} since has no subscriptions", unregister.Actor)));
                Context.Unwatch(unregister.Actor);
            }).With<Terminated>(terminated =>
            {
                if (_debug)
                    _eventStream.Publish(new Debug(this.GetType().Name, GetType(),
                        string.Format("unsubscribe {0} from {1}, because it was terminated", terminated.Actor , _eventStream )));
                _eventStream.Unsubscribe(terminated.Actor);
            })
            .WasHandled;
        }

        protected override void PreStart()
        {
            if (_debug)
                _eventStream.Publish(new Debug(this.GetType().Name, GetType(),
                    string.Format("registering unsubscriber with {0}", _eventStream)));
            _eventStream.InitUnsubscriber(Self, _system);
        }

        internal class Register
        {
            public Register(IActorRef actor)
            {
                Actor = actor;
            }

            public IActorRef Actor { get; private set; }
        }


        internal class Terminated
        {
            public Terminated(IActorRef actor)
            {
                Actor = actor;
            }

            public IActorRef Actor { get; private set; }
        }

        internal class UnregisterIfNoMoreSubscribedChannels
        {
            public UnregisterIfNoMoreSubscribedChannels(IActorRef actor)
            {
                Actor = actor;
            }

            public IActorRef Actor { get; private set; }
        }
    }



    /// <summary>
    /// Provides factory for Akka.Event.EventStreamUnsubscriber actors with unique names.
    /// This is needed if someone spins up more EventStreams using the same ActorSystem,
    /// each stream gets it's own unsubscriber.
    /// </summary>
    class EventStreamUnsubscribersProvider
    {
        private readonly AtomicCounter _unsubscribersCounter = new AtomicCounter(0);
        private static readonly EventStreamUnsubscribersProvider _instance = new EventStreamUnsubscribersProvider();


        public static EventStreamUnsubscribersProvider Instance
        {
            get { return _instance; }
        }

        public void Start(ActorSystem system, EventStream eventStream, bool debug)
        {
            system.ActorOf(Props.Create<EventStreamUnsubscriber>(eventStream, system, debug),
                string.Format("EventStreamUnsubscriber-{0}", _unsubscribersCounter.IncrementAndGet()));
        }
    }
}
