using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class EventStream 
    {
        private ConcurrentDictionary<Subscriber, Classifier> subscribers = new ConcurrentDictionary<Subscriber, Classifier>();
        private bool debug;

        public EventStream(bool debug)
        {
            this.debug = debug;
        }

        public void Subscribe(Subscriber subscriber)
        {
            subscribers.TryAdd(subscriber, Classifier.EveryClassifier);
        }

        public void Subscribe(Subscriber subscriber,Classifier classifier)
        {
            subscribers.TryAdd(subscriber, classifier);
        }

        public void Unsubscribe(Subscriber subscriber)
        {
            Classifier tmp;
            subscribers.TryRemove(subscriber, out tmp);
        }

        public void Publish(EventMessage @event)
        {
            foreach(var kvp in subscribers)
            {
                var subscriber = kvp.Key;
                var classifier = kvp.Value;

                if (classifier.Classify(@event))
                {
                    subscriber.Publish(@event);
                }
            }
        }
    }

    public abstract class Classifier
    {
        public static readonly Classifier EveryClassifier = new EveryClassifier();

        public abstract bool Classify(EventMessage @event);
    }

    public class EveryClassifier : Classifier
    {
        public override bool Classify(EventMessage @event)
        {
            return true;
        }
    }

    public class SubClassifier : Classifier
    {
        private Type type;
        public SubClassifier(Type type)
        {
            this.type = type;
        }
        public override bool Classify(EventMessage @event)
        {
            return type.IsAssignableFrom(@event.GetType());
        }
    }

    public class SubClassifier<T> : SubClassifier
    {
        public SubClassifier() : base(typeof(T))
        { }
    }

    public abstract class Subscriber
    {
        public abstract void Publish(EventMessage @event);

        public static implicit operator Subscriber(ActorRef actor)
        {
            return new ActorSubscriber(actor);
        }       
    }

    public class ActorSubscriber : Subscriber
    {
        private ActorRef actor;
        public ActorSubscriber(ActorRef actor)
        {
            this.actor = actor;
        }
        public override void Publish(EventMessage @event)
        {
            actor.Tell(@event);
        }        
    }

    public class BlockingCollectionSubscriber : Subscriber
    {
        private BlockingCollection<EventMessage> queue;
        public BlockingCollectionSubscriber(BlockingCollection<EventMessage> queue)
        {
            this.queue = queue;
        }
        public override void Publish(EventMessage @event)
        {
            this.queue.Add(@event);
        }
    }

    public class ActionSubscriber : Subscriber
    {
        private Action<EventMessage> action;
        public ActionSubscriber (Action<EventMessage> action)
        {
            this.action = action;
        }

        public override void Publish(EventMessage @event)
        {
            action(@event);
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
