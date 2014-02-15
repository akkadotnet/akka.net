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
                //remove sub-subscribers
                foreach(var kvp in subscribers)
                {
                    if (IsSubClassification(classifier,kvp.Key))
                    {
                        kvp.Value.RemoveAll(s => s.Subscriber.Equals(subscriber));
                        //TODO: unsubscriptions in the subscriptions needs to be carried over to the new parent subscription
                    }
                }

                List<Subscription<TSubscriber, TClassifier>> set;
                if (!subscribers.TryGetValue(classifier, out set))
                {
                    set = new List<Subscription<TSubscriber, TClassifier>>();
                    subscribers.Add(classifier, set);
                }
                set.Add(new Subscription<TSubscriber,TClassifier>(subscriber));
                ClearCache();
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
                ClearCache();
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
                else
                {
                    foreach (var kvp in subscribers)
                    {
                        if (IsSubClassification(kvp.Key,classifier))
                        {
                            var s = kvp.Value;
                            var subscriptions = s.Where(ss => ss.Subscriber.Equals(subscriber)).ToList();
                            foreach(var sss in subscriptions)
                            {
                                sss.Unsubscriptions.Add(classifier);
                            }
                        }
                    }
                }
                ClearCache();
            }
        }

        private void ClearCache()
        {
            cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();
        }

        protected abstract bool IsSubClassification(TClassifier parent, TClassifier child);

        protected abstract void Publish(TEvent @event,TSubscriber subscriber);

        protected abstract bool Classify(TEvent @event, TClassifier classifier);

        protected abstract TClassifier GetClassifier(TEvent @event);

        private volatile ConcurrentDictionary<TClassifier, List<TSubscriber>> cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();

        public virtual void Publish(TEvent @event)
        {
            var eventClass = GetClassifier(@event);

            List<TSubscriber> cachedSubscribers;
            if (cache.TryGetValue(eventClass, out cachedSubscribers))
            {
                PublishToSubscribers(@event, cachedSubscribers);
            }
            else
            {
                cachedSubscribers = UpdateCacheForEventClassifier(@event, eventClass, cachedSubscribers);
                PublishToSubscribers(@event, cachedSubscribers);
            }
        }

        private void PublishToSubscribers(TEvent @event, List<TSubscriber> cachedSubscribers)
        {
            foreach (var subscriber in cachedSubscribers)
            {
                this.Publish(@event, subscriber);
            }
        }

        private List<TSubscriber> UpdateCacheForEventClassifier(TEvent @event, TClassifier eventClass, List<TSubscriber> cachedSubscribers)
        {
            lock (subscribers)
            {
                cachedSubscribers = new List<TSubscriber>();
                foreach (var kvp in subscribers)
                {
                    var classifier = kvp.Key;
                    var set = kvp.Value;
                    if (Classify(@event, classifier))
                    {
                        foreach (var subscriber in set)
                        {
                            if (subscriber.Unsubscriptions.Any(u => IsSubClassification(u, eventClass)))
                                continue;

                            cachedSubscribers.Add(subscriber.Subscriber);
                        }
                    }
                }

                cache[eventClass] = cachedSubscribers;
            }
            return cachedSubscribers;
        }
    }

    public abstract class ActorEventBus<TEvent,TClassifier> : EventBus<TEvent,TClassifier,ActorRef>
    {

    }
}
