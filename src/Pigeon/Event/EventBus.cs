using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Event
{
    public class Subscription<TSubscriber, TClassifier>
    {
        public Subscription(TSubscriber subscriber, IEnumerable<TClassifier> unsubscriptions)
        {
            Subscriber = subscriber;
            Unsubscriptions = new HashSet<TClassifier>(unsubscriptions);
        }

        public Subscription(TSubscriber subscriber)
        {
            Subscriber = subscriber;
            Unsubscriptions = new HashSet<TClassifier>();
        }

        public TSubscriber Subscriber { get; private set; }
        public ISet<TClassifier> Unsubscriptions { get; private set; }
    }

    public abstract class EventBus<TEvent, TClassifier, TSubscriber>
    {
        private readonly Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>> classifiers =
            new Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>>();

        private volatile ConcurrentDictionary<TClassifier, List<TSubscriber>> cache =
            new ConcurrentDictionary<TClassifier, List<TSubscriber>>();

        protected string SimpleName(object source)
        {
            return SimpleName(source.GetType());
        }

        protected string SimpleName(Type source)
        {
            return source.Name;
        }

        public virtual bool Subscribe(TSubscriber subscriber, TClassifier classifier)
        {
            lock (classifiers)
            {
                List<Subscription<TSubscriber, TClassifier>> subscribers;
                if (!classifiers.TryGetValue(classifier, out subscribers))
                {
                    subscribers = new List<Subscription<TSubscriber, TClassifier>>();
                    classifiers.Add(classifier, subscribers);
                }
                //already subscribed
                if (subscribers.Any(s => s.Subscriber.Equals(subscriber)))
                    return false;

                var subscription = new Subscription<TSubscriber, TClassifier>(subscriber);

                subscribers.Add(subscription);
                ClearCache();
                return true;
            }
        }

        public virtual bool Unsubscribe(TSubscriber subscriber)
        {
            lock (classifiers)
            {
                bool res = false;
                List<Subscription<TSubscriber, TClassifier>> subscribers;
                foreach (TClassifier classifier in classifiers.Keys)
                {
                    if (classifiers.TryGetValue(classifier, out subscribers))
                    {
                        if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                            res = true;
                    }
                }
                ClearCache();
                return res;
            }
        }

        public virtual bool Unsubscribe(TSubscriber subscriber, TClassifier classifier)
        {
            lock (classifiers)
            {
                bool res = false;
                List<Subscription<TSubscriber, TClassifier>> subscribers;
                if (classifiers.TryGetValue(classifier, out subscribers))
                {
                    if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                        res = true;
                }
                else
                {
                    foreach (var kvp in classifiers)
                    {
                        if (IsSubClassification(kvp.Key, classifier))
                        {
                            List<Subscription<TSubscriber, TClassifier>> s = kvp.Value;
                            List<Subscription<TSubscriber, TClassifier>> subscriptions =
                                s.Where(ss => ss.Subscriber.Equals(subscriber)).ToList();
                            foreach (var existingSubscriber in subscriptions)
                            {
                                existingSubscriber.Unsubscriptions.Add(classifier);
                                res = true;
                            }
                        }
                    }
                }
                ClearCache();
                return res;
            }
        }

        private void ClearCache()
        {
            cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();
        }

        protected abstract bool IsSubClassification(TClassifier parent, TClassifier child);

        protected abstract void Publish(TEvent @event, TSubscriber subscriber);

        protected abstract bool Classify(TEvent @event, TClassifier classifier);

        protected abstract TClassifier GetClassifier(TEvent @event);

        public virtual void Publish(TEvent @event)
        {
            TClassifier eventClass = GetClassifier(@event);

            List<TSubscriber> cachedSubscribers;
            if (cache.TryGetValue(eventClass, out cachedSubscribers))
            {
                PublishToSubscribers(@event, cachedSubscribers);
            }
            else
            {
                cachedSubscribers = UpdateCacheForEventClassifier(@event, eventClass);
                PublishToSubscribers(@event, cachedSubscribers);
            }
        }

        private void PublishToSubscribers(TEvent @event, List<TSubscriber> cachedSubscribers)
        {
            foreach (TSubscriber subscriber in cachedSubscribers)
            {
                Publish(@event, subscriber);
            }
        }

        private List<TSubscriber> UpdateCacheForEventClassifier(TEvent @event, TClassifier eventClass)
        {
            lock (classifiers)
            {
                var cachedSubscribers = new HashSet<TSubscriber>();
                foreach (var kvp in classifiers)
                {
                    TClassifier classifier = kvp.Key;
                    List<Subscription<TSubscriber, TClassifier>> set = kvp.Value;
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
                //finds a distinct list of subscribers for the given event type
                List<TSubscriber> list = cachedSubscribers.ToList();
                cache[eventClass] = list;
                return list;
            }
        }
    }

    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, ActorRef>
    {
    }
}