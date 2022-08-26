//-----------------------------------------------------------------------
// <copyright file="EventBusUnsubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Annotations;

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
    internal class EventStreamUnsubscriber : ActorBase
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
        
        protected override bool Receive(object message)
        {
            switch (message)
            {
                case Register register:
                {
                    if (_debug)
                        _eventStream.Publish(new Debug(GetType().Name, GetType(),
                            $"watching {register.Actor} in order to unsubscribe from EventStream when it terminates"));
                    Context.Watch(register.Actor);
                    break;
                }
                case UnregisterIfNoMoreSubscribedChannels unregister:
                {
                    if (_debug)
                        _eventStream.Publish(new Debug(GetType().Name, GetType(),
                            $"unwatching {unregister.Actor} since has no subscriptions"));
                    Context.Unwatch(unregister.Actor);
                    break;
                }
                case Terminated terminated:
                {
                    if (_debug)
                        _eventStream.Publish(new Debug(GetType().Name, GetType(),
                            $"unsubscribe {terminated.ActorRef} from {_eventStream}, because it was terminated"));
                    _eventStream.Unsubscribe(terminated.ActorRef);
                    break;
                }
                default:
                    return false;
            }

            return true;
        }

        protected override void PreStart()
        {
            if (_debug)
                _eventStream.Publish(new Debug(GetType().Name, GetType(),
                    string.Format("registering unsubscriber with {0}", _eventStream)));
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Registers a new subscriber to be death-watched and automatically unsubscribed.
        /// </summary>
        internal class Register : INoSerializationVerificationNeeded
        {
            public Register(IActorRef actor)
            {
                Actor = actor;
            }

            /// <summary>
            /// The actor we're going to deathwatch and automatically unsubscribe
            /// </summary>
            public IActorRef Actor { get; private set; }
        }

        /// <summary>
        /// INTERNAL API
        ///
        /// Unsubscribes an actor that is no longer subscribed and does not need to be death-watched any longer.
        /// </summary>
        internal class UnregisterIfNoMoreSubscribedChannels : INoSerializationVerificationNeeded
        {
            public UnregisterIfNoMoreSubscribedChannels(IActorRef actor)
            {
                Actor = actor;
            }

            /// <summary>
            /// The actor we're no longer going to death watch.
            /// </summary>
            public IActorRef Actor { get; private set; }
        }
    }
}
