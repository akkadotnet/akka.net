using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Akka.Actor;

namespace Akka.Event
{


    /// <summary>
    /// Class EventBus.
    /// </summary>
    /// <typeparam name="TEvent">The type of the t event.</typeparam>
    /// <typeparam name="TClassifier">The type of the t classifier.</typeparam>
    /// <typeparam name="TSubscriber">The type of the t subscriber.</typeparam>
    public abstract class EventBus<TEvent, TClassifier, TSubscriber>
    {
        /// <summary>
        /// The classifiers
        /// </summary>
        private readonly Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>> classifiers =
            new Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>>();

        /// <summary>
        /// The cache
        /// </summary>
        private volatile ConcurrentDictionary<TClassifier, List<TSubscriber>> cache =
            new ConcurrentDictionary<TClassifier, List<TSubscriber>>();

        /// <summary>
        /// Simples the name.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>System.String.</returns>
        protected string SimpleName(object source)
        {
            return SimpleName(source.GetType());
        }

        /// <summary>
        /// Simples the name.
        /// </summary>
        /// <param name="source">The source.</param>
        /// <returns>System.String.</returns>
        protected string SimpleName(Type source)
        {
            return source.Name;
        }

        /// <summary>
        /// Subscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="classifier">The classifier.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Unsubscribes the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="classifier">The classifier.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
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

        /// <summary>
        /// Clears the cache.
        /// </summary>
        private void ClearCache()
        {
            cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();
        }

        /// <summary>
        /// Determines whether [is sub classification] [the specified parent].
        /// </summary>
        /// <param name="parent">The parent.</param>
        /// <param name="child">The child.</param>
        /// <returns><c>true</c> if [is sub classification] [the specified parent]; otherwise, <c>false</c>.</returns>
        protected abstract bool IsSubClassification(TClassifier parent, TClassifier child);

        /// <summary>
        /// Publishes the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="subscriber">The subscriber.</param>
        protected abstract void Publish(TEvent @event, TSubscriber subscriber);

        /// <summary>
        /// Classifies the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="classifier">The classifier.</param>
        /// <returns><c>true</c> if XXXX, <c>false</c> otherwise.</returns>
        protected abstract bool Classify(TEvent @event, TClassifier classifier);

        /// <summary>
        /// Gets the classifier.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <returns>`1.</returns>
        protected abstract TClassifier GetClassifier(TEvent @event);

        /// <summary>
        /// Publishes the specified event.
        /// </summary>
        /// <param name="event">The event.</param>
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

        /// <summary>
        /// Publishes to subscribers.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="cachedSubscribers">The cached subscribers.</param>
        private void PublishToSubscribers(TEvent @event, List<TSubscriber> cachedSubscribers)
        {
            foreach (TSubscriber subscriber in cachedSubscribers)
            {
                Publish(@event, subscriber);
            }
        }

        /// <summary>
        /// Updates the cache for event classifier.
        /// </summary>
        /// <param name="event">The event.</param>
        /// <param name="eventClass">The event class.</param>
        /// <returns>List{`2}.</returns>
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

    /// <summary>
    /// Class ActorEventBus.
    /// </summary>
    /// <typeparam name="TEvent">The type of the t event.</typeparam>
    /// <typeparam name="TClassifier">The type of the t classifier.</typeparam>
    public abstract class ActorEventBus<TEvent, TClassifier> : EventBus<TEvent, TClassifier, ActorRef>
    {
    }
}