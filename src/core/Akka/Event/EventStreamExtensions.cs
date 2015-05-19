using Akka.Actor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Event
{

    /// <summary>
    /// Extension methods for the EventStream class.
    /// </summary>
    public static class EventStreamExtensions
    {
        /// <summary>
        /// Subscribes the specified subscriber.
        /// </summary>
        /// <typeparam name="TChannel">The channel.</typeparam>
        /// <param name="eventStream">The event stream.</param>
        /// <param name="subscriber">The subscriber.</param>
        /// <returns><c>true</c> if subscription was successful, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public static bool Subscribe<TChannel>(this EventStream eventStream, IActorRef subscriber)
        {
            return eventStream.Subscribe(subscriber, typeof(TChannel));
        }

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <typeparam name="TChannel">The channel.</typeparam>
        /// <param name="eventStream">The event stream.</param>
        /// <param name="subscriber">The subscriber.</param>
        /// <returns><c>true</c> if unsubscription was successful, <c>false</c> otherwise.</returns>
        /// <exception cref="System.ArgumentNullException">subscriber</exception>
        public static bool Unsubscribe<TChannel>(this EventStream eventStream, IActorRef subscriber)
        {
            return eventStream.Unsubscribe(subscriber, typeof(TChannel));
        }
    }
}
