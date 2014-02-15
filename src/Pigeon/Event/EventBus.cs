using Pigeon.Actor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Pigeon.Event
{
    public class Subscription<TSubscriber,TClassifier>
    {
        public Subscription(TSubscriber subscriber)
        {
            this.Subscriber = subscriber;
            this.Unsubscriptions = new HashSet<TClassifier>();
        }
        public TSubscriber Subscriber { get;private set; }
        public ISet<TClassifier> Unsubscriptions { get;private set; }
    }
    public abstract class EventBus<TEvent,TClassifier,TSubscriber>
    {
        private Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>> subscribers = new Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>>();

        protected string SimpleName(object source)
        {
            return source.GetType().Name;
        }

        public virtual void Subscribe(TSubscriber subscriber, TClassifier classifier)
        {            
            lock(subscribers)
            {
                List<Subscription<TSubscriber, TClassifier>> set;
                if (!subscribers.TryGetValue(classifier, out set))
                {
                    set = new List<Subscription<TSubscriber, TClassifier>>();
                    subscribers.Add(classifier, set);
                }
                set.Add(new Subscription<TSubscriber,TClassifier>(subscriber));
            }
        }

        public virtual void Unsubscribe(TSubscriber subscriber)
        {
            lock (subscribers)
            {
                List<Subscription<TSubscriber, TClassifier>> set;
                foreach(var classifier in subscribers.Keys)
                {
                    if (subscribers.TryGetValue(classifier, out set))
                    {
                        set.RemoveAll(s => s.Subscriber.Equals(subscriber));
                    }
                }
            }
        }

        public virtual void Unsubscribe(TSubscriber subscriber,TClassifier classifier)
        {
            lock (subscribers)
            {
                List<Subscription<TSubscriber,TClassifier>> set;
                if (subscribers.TryGetValue(classifier, out set))
                {
                    set.RemoveAll(s => s.Subscriber.Equals(subscriber));
                }
            }
        }

        protected abstract bool IsSubClassification(TClassifier parent, TClassifier child);

        protected abstract void Publish(TEvent @event,TSubscriber subscriber);

        protected abstract bool Classify(TEvent @event, TClassifier classifier);        

        public virtual void Publish(TEvent @event)
        {
            foreach (var kvp in subscribers)
            {
                var classifier = kvp.Key;
                var set = kvp.Value;
                if (Classify(@event, classifier))
                {
                    foreach (var subscriber in set)
                    {
                        this.Publish(@event, subscriber.Subscriber);
                    }
                }                
            }
        }
    }
}
