using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class EventStream : LoggingBus
    {
        private bool debug;
        public EventStream(bool debug)
        {
            this.debug = debug;
        }

        public override bool Subscribe(ActorRef subscriber,Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "subscribing " + subscriber + " to channel " + channel));
            return base.Subscribe(subscriber,channel);
        }

        public override bool Unsubscribe(ActorRef subscriber,Type channel)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            var res = base.Unsubscribe(subscriber,channel);
            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "unsubscribing " + subscriber + " from channel " + channel));
            return res;
        }

        public override bool Unsubscribe(ActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            bool res = base.Unsubscribe(subscriber);
            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "unsubscribing " + subscriber + " from all channels"));
            return res;
        }

        protected override void Publish(object @event, ActorRef subscriber)
        {
            subscriber.Tell(@event);
        }

        protected override bool Classify(object @event,Type classifier)
        {
            return (classifier.IsAssignableFrom(@event.GetType()));
        }

        protected override bool IsSubClassification(Type parent, Type child)
        {
            return parent.IsAssignableFrom(child);
        }

        protected override Type GetClassifier(object @event)
        {
            return @event.GetType();
        }
    }      
}
