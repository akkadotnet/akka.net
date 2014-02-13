using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class EventStream : EventBus
    {
        private bool debug;
        public EventStream(bool debug)
        {
            this.debug = debug;
        }

        public void Subscribe(ActorRef subscriber,Type type)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            var channel = new SubClassifier(type);
            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "subscribing " + subscriber + " to channel " + channel));
            base.Subscribe(subscriber,channel);
        }

        public void Unsubscribe(ActorRef subscriber,Type type)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            var channel = new SubClassifier(type);
            base.Unsubscribe(subscriber,channel);
            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "unsubscribing " + subscriber + " from channel " + channel));            
        }

        public void Unsubscribe(ActorRef subscriber)
        {
            if (subscriber == null)
                throw new ArgumentNullException("subscriber is null");

            base.Unsubscribe(subscriber);
            if (debug) Publish(new Debug(SimpleName(this), this.GetType(), "unsubscribing " + subscriber + " from all channels"));
        }
    }

    

    public static class EventStreamExtensions
    {
        public static void Subscribe(this EventStream self, Subscriber subscriber,Type type)
        {
            self.Subscribe(subscriber, new SubClassifier(type));
        }

        public static void Subscribe(this EventStream self, Action<EventMessage> action)
        {
            self.Subscribe(new ActionSubscriber(action));
        }

        public static void Subscribe(this EventStream self, Action<EventMessage> action,Type type)
        {
            self.Subscribe(new ActionSubscriber(action),new SubClassifier(type));
        }
    }
}
