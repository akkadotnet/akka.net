using System;
using Akka.Actor;

namespace Akka.Event
{
    public class EventStream : LoggingBus
    {
        private readonly bool debug;

        public EventStream(bool debug)
        {
            this.debug = debug;
        }

        public override bool Subscribe(ActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            if (debug)
                Publish(new Debug(SimpleName(this), GetType(), "subscribing " + subscriber + " to channel " + channel));
            return base.Subscribe(subscriber, channel);
        }

        public override bool Unsubscribe(ActorRef subscriber, Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            bool res = base.Unsubscribe(subscriber, channel);
            if (debug)
                Publish(new Debug(SimpleName(this), GetType(),
                    "unsubscribing " + subscriber + " from channel " + channel));
            return res;
        }

        public override bool Unsubscribe(ActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            bool res = base.Unsubscribe(subscriber);
            if (debug)
                Publish(new Debug(SimpleName(this), GetType(), "unsubscribing " + subscriber + " from all channels"));
            return res;
        }
    }
}