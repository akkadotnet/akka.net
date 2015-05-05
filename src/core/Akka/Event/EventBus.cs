//-----------------------------------------------------------------------
// <copyright file="EventBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    /// <summary>
    /// Represents the base event bus, internally manages subscriptions using the event type, classifier type and subscriber type.
    /// </summary>
    /// <typeparam name="TEvent">The type of the event.</typeparam>
    /// <typeparam name="TClassifier">The type of the classifier.</typeparam>
    /// <typeparam name="TSubscriber">The type of the subscriber.</typeparam>
    public abstract class EventBus<TEvent, TClassifier, TSubscriber>
    {
        private readonly Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>> _classifiers =
            new Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>>();

        private volatile ConcurrentDictionary<TClassifier, List<TSubscriber>> _cache =
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
            lock (_classifiers)
            {
                List<Subscription<TSubscriber, TClassifier>> subscribers;
                if (!_classifiers.TryGetValue(classifier, out subscribers))
                {
                    subscribers = new List<Subscription<TSubscriber, TClassifier>>();
                    _classifiers.Add(classifier, subscribers);
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
            lock (_classifiers)
            {
                var res = false;

                foreach (var classifier in _classifiers.Keys)
                {
                    List<Subscription<TSubscriber, TClassifier>> subscribers;
                    if (!_classifiers.TryGetValue(classifier, out subscribers)) 
                        continue;
                    
                    if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                        res = true;
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
            lock (_classifiers)
            {
                var res = false;

                List<Subscription<TSubscriber, TClassifier>> subscribers;
                if (_classifiers.TryGetValue(classifier, out subscribers))
                {
                    if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                        res = true;
                }
                else
                {
                    foreach (var kvp in _classifiers)
                    {
                        if (!IsSubClassification(kvp.Key, classifier)) 
                            continue;

                        var subscriptions = kvp.Value.Where(ss => ss.Subscriber.Equals(subscriber)).ToList();
                        foreach (var existingSubscriber in subscriptions)
                        {
                            existingSubscriber.Unsubscriptions.Add(classifier);
                            res = true;
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
            _cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();
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
            var eventClass = GetClassifier(@event);

            List<TSubscriber> cachedSubscribers;
            if (_cache.TryGetValue(eventClass, out cachedSubscribers))
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
            foreach (var subscriber in cachedSubscribers)
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
            lock (_classifiers)
            {
                var cachedSubscribers = new HashSet<TSubscriber>();

                foreach (var kvp in _classifiers)
                {
                    var classifier = kvp.Key;
                    var set = kvp.Value;

                    if (!Classify(@event, classifier)) 
                        continue;

                    foreach (var subscriber in set)
                    {
                        if (subscriber.Unsubscriptions.Any(u => IsSubClassification(u, eventClass)))
                            continue;

                        cachedSubscribers.Add(subscriber.Subscriber);
                    }
                }

                //finds a distinct list of subscribers for the given event type
                var list = cachedSubscribers.ToList();
                _cache[eventClass] = list;
                
                return list;
            }
        }
    }
}

