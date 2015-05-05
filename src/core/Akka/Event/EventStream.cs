//-----------------------------------------------------------------------
// <copyright file="EventStream.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;

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
        /// <summary>
        /// Determines if subscription logging is enabled.
        /// </summary>
        private readonly bool _debug;

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
        /// <returns><c>true</c> if subscription was successful, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Subscribe(IActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "subscribing " + subscriber + " to channel " + channel));
            }

            return base.Subscribe(subscriber, channel);
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="channel">The channel.</param>
        /// <returns><c>true</c> if unsubscription was successful, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Unsubscribe(IActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from channel " + channel));
            }

            return base.Unsubscribe(subscriber, channel);
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <returns><c>true</c> if unsubscription was successful, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Unsubscribe(IActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            if (_debug)
            {
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from all channels"));
            }

            return base.Unsubscribe(subscriber);
        }
    }
}

