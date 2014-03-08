using System;
using Akka.Actor;

namespace Akka.Event
{
    /// <summary>
    /// Class EventStream.
    /// </summary>
    public class EventStream : LoggingBus
    {
        /// <summary>
        /// Determines if subscription logging is enabled
        /// </summary>
        private readonly bool debug;

        /// <summary>
        /// Initializes a new instance of the <see cref="EventStream"/> class.
        /// </summary>
        /// <param name="debug">if set to <c>true</c> [debug].</param>
        public EventStream(bool debug)
        {
            this.debug = debug;
        }

        /// <summary>
        /// Subscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="channel">The channel.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Subscribe(ActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            if (debug)
                Publish(new Debug(SimpleName(this), GetType(), "subscribing " + subscriber + " to channel " + channel));
            return base.Subscribe(subscriber, channel);
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="channel">The channel.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Unsubscribe(ActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            bool res = base.Unsubscribe(subscriber, channel);
            if (debug)
                Publish(new Debug(SimpleName(this), GetType(),
                    "unsubscribing " + subscriber + " from channel " + channel));
            return res;
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public override bool Unsubscribe(ActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber");

            bool res = base.Unsubscribe(subscriber);
            if (debug)
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from all channels"));
            return res;
        }
    }
}