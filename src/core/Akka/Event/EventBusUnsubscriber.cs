//-----------------------------------------------------------------------
// <copyright file="EventBusUnsubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Annotations;
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
    [InternalApi]
    class EventStreamUnsubscriber : ActorBase
    {
        private readonly EventStream _eventStream;
        private readonly bool _debug;
        private readonly ActorSystem _system;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="eventStream">TBD</param>
        /// <param name="system">TBD</param>
        /// <param name="debug">TBD</param>
        public EventStreamUnsubscriber(EventStream eventStream, ActorSystem system, bool debug)
        {
            _eventStream = eventStream;
            _system = system;
            _debug = debug;
           
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="message">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            if (_debug)
                _eventStream.Publish(new Debug(this.GetType().Name, GetType(),
                    string.Format("registering unsubscriber with {0}", _eventStream)));
            _eventStream.InitUnsubscriber(Self, _system);
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class Register
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actor">TBD</param>
            public Register(IActorRef actor)
            {
                Actor = actor;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Actor { get; private set; }
        }


        /// <summary>
        /// TBD
        /// </summary>
        internal class Terminated
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actor">TBD</param>
            public Terminated(IActorRef actor)
            {
                Actor = actor;
            }

            /// <summary>
            /// TBD
            /// </summary>
            public IActorRef Actor { get; private set; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        internal class UnregisterIfNoMoreSubscribedChannels
        {
            /// <summary>
            /// TBD
            /// </summary>
            /// <param name="actor">TBD</param>
            public UnregisterIfNoMoreSubscribedChannels(IActorRef actor)
            {
                Actor = actor;
            }

            /// <summary>
            /// TBD
            /// </summary>
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


        /// <summary>
        /// TBD
        /// </summary>
        public static EventStreamUnsubscribersProvider Instance
        {
            get { return _instance; }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <param name="eventStream">TBD</param>
        /// <param name="debug">TBD</param>
        public void Start(ActorSystemImpl system, EventStream eventStream, bool debug)
        {
            system.SystemActorOf(Props.Create<EventStreamUnsubscriber>(eventStream, system, debug),
                string.Format("EventStreamUnsubscriber-{0}", _unsubscribersCounter.IncrementAndGet()));
        }
    }
}
