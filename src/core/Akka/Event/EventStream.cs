//-----------------------------------------------------------------------
// <copyright file="EventStream.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Dispatch;
using Akka.Util;
using Akka.Util.Internal;


namespace Akka.Event
{
    /// <summary>
    /// The EventStream is a pub-sub stream of events that can be both system and user generated. 
    /// 
    /// The subscribers are IActorRef instances and events can be any object. Subscriptions are hierarchical meaning that if you listen to
    /// an event for a particular type you will receive events for that type and any sub types.
    /// 
    /// If the debug flag is activated any operations on the event stream will be published as debug level events.
    /// </summary>
    public class EventStream : LoggingBus
    {
        private readonly bool _debug;

        // used to uniquely name unsubscribers instances, should there be more than one ActorSystem / EventStream
        private static readonly AtomicCounter UnsubscribersCounter = new(0);
        private readonly AtomicReference<IActorRef> _unsubscriber = new(ActorRefs.NoSender);
        
        // in the event that an actor subscribers to the EventStream prior to ActorSystemImpl.Init is called
        // we register them here and then move them all
        private readonly ConcurrentSet<IActorRef> _pendingUnsubscribers = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="EventStream"/> class.
        /// </summary>
        /// <param name="debug">if set to <c>true</c> [debug].</param>
        public EventStream(bool debug)
        {
            _debug = debug;
        }

        /// <summary>
        /// Subscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="channel">The channel.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="subscriber"/> is undefined.
        /// </exception>
        /// <returns><c>true</c> if subscription was successful, <c>false</c> otherwise.</returns>
        public override bool Subscribe(IActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber), "The subscriber cannot be a null actor.");

            RegisterWithUnsubscriber(subscriber);
            var res = base.Subscribe(subscriber, channel);
            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "subscribing " + subscriber + " to channel " + channel));
            }
            return res;
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="channel">The channel.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="subscriber"/> is undefined.
        /// </exception>
        /// <returns><c>true</c> if unsubscription was successful, <c>false</c> otherwise.</returns>
        public override bool Unsubscribe(IActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber), "The subscriber cannot be a null actor.");

            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from channel " + channel));
            }
            UnregisterIfNoMoreSubscribedChannels(subscriber);
            return base.Unsubscribe(subscriber, channel);
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown if the given <paramref name="subscriber"/> is undefined.
        /// </exception>
        /// <returns><c>true</c> if unsubscription was successful, <c>false</c> otherwise.</returns>
        public override bool Unsubscribe(IActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException(nameof(subscriber), "The subscriber cannot be a null actor.");

            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from all channels"));
            }
            UnregisterIfNoMoreSubscribedChannels(subscriber);
            return base.Unsubscribe(subscriber);
        }

        /// <summary>
        /// Used to start the Unsubscriber actor, responsible for garabage-collecting
        /// all expired subscriptions when the subscribed actor terminates.
        /// </summary>
        /// <param name="system">TBD</param>
        public void StartUnsubscriber(ActorSystemImpl system)
        {
            if (_unsubscriber.Value.IsNobody())
            {
                lock (this)
                {
                    // not started
                    var currentValue = _unsubscriber.Value;
                    var unsubscriber= system.SystemActorOf(Props.Create<EventStreamUnsubscriber>(this, system, _debug).WithDispatcher(Dispatchers.InternalDispatcherId),
                        $"EventStreamUnsubscriber-{UnsubscribersCounter.IncrementAndGet()}");

                    if (_unsubscriber.CompareAndSet(currentValue, unsubscriber))
                    {
                        // backfill all pending unsubscribers
                        foreach (var s in _pendingUnsubscribers)
                        {
                            unsubscriber.Tell(new EventStreamUnsubscriber.Register(s));
                        }
                        _pendingUnsubscribers.Clear();
                    }
                    else
                    {
                        // somehow, despite being locked, we managed to lose the compare and swap
                        if (_unsubscriber.Value.IsNobody())
                            throw new IllegalActorStateException("EventStream is corrupted");
                    }
                }
            }
        }

        private void RegisterWithUnsubscriber(IActorRef subscriber)
        {
            if (_unsubscriber.Value.IsNobody())
            {
                // pending
                _pendingUnsubscribers.TryAdd(subscriber);
            }
            else
            {
                _unsubscriber.Value.Tell( new EventStreamUnsubscriber.Register(subscriber));
            }
            
        }

        private void UnregisterIfNoMoreSubscribedChannels(IActorRef subscriber)
        {
            // not an important operation. If we fail to process this message due to a race condition, then the
            // death watch subscription is a no-op anyway.
            if (!_unsubscriber.Value.IsNobody())
            {
                _unsubscriber.Value.Tell(new EventStreamUnsubscriber.UnregisterIfNoMoreSubscribedChannels(subscriber));
            }
        }
    }
}
