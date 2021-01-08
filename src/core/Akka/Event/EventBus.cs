//-----------------------------------------------------------------------
// <copyright file="EventBus.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Akka.Event
{
    /// <summary>
    /// This class provides base publish/subscribe functionality for working with events inside the system.
    /// </summary>
    /// <typeparam name="TEvent">The type of event published to the bus.</typeparam>
    /// <typeparam name="TClassifier">The type of classifier used to classify events.</typeparam>
    /// <typeparam name="TSubscriber">The type of the subscriber that listens for events.</typeparam>
    public abstract class EventBus<TEvent, TClassifier, TSubscriber>
    {
        private readonly Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>> _classifiers =
            new Dictionary<TClassifier, List<Subscription<TSubscriber, TClassifier>>>();

        private volatile ConcurrentDictionary<TClassifier, List<TSubscriber>> _cache =
            new ConcurrentDictionary<TClassifier, List<TSubscriber>>();

        /// <summary>
        /// Retrieves the simplified type name (the class name without the namespace) of a given object.
        /// </summary>
        /// <param name="source">The object that is being queried.</param>
        /// <returns>The simplified type name of the given object.</returns>
        protected string SimpleName(object source)
        {
            return SimpleName(source.GetType());
        }

        /// <summary>
        /// Retrieves the simplified type name (the class name without the namespace) of a given type.
        /// </summary>
        /// <param name="source">The object that is being queried.</param>
        /// <returns>The simplified type name of the given type.</returns>
        protected string SimpleName(Type source)
        {
            return source.Name;
        }

        /// <summary>
        /// Adds the specified subscriber to the list of subscribers that listen for particular events on the bus.
        /// </summary>
        /// <param name="subscriber">The subscriber that is being added.</param>
        /// <param name="classifier">The classifier of the event that the subscriber wants.</param>
        /// <returns><c>true</c> if the subscription succeeds; otherwise <c>false</c>.</returns>
        public virtual bool Subscribe(TSubscriber subscriber, TClassifier classifier)
        {
            lock (_classifiers)
            {
                if (!_classifiers.TryGetValue(classifier, out var subscribers))
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
        /// Removes the specified subscriber from the list of subscribers that listen for particular events on the bus.
        /// </summary>
        /// <param name="subscriber">The subscriber that is being removed.</param>
        /// <returns><c>true</c> if the subscription cancellation succeeds; otherwise <c>false</c>.</returns>
        public virtual bool Unsubscribe(TSubscriber subscriber)
        {
            lock (_classifiers)
            {
                var res = false;

                foreach (var classifier in _classifiers.Keys)
                {
                    if (!_classifiers.TryGetValue(classifier, out var subscribers)) 
                        continue;
                    
                    if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                        res = true;
                }
                
                ClearCache();
                return res;
            }
        }

        /// <summary>
        /// Removes the specified subscriber from the list of subscribers that listen for particular events on the bus.
        /// </summary>
        /// <param name="subscriber">The subscriber that is being removed.</param>
        /// <param name="classifier">The classifier of the event that the subscriber wants.</param>
        /// <returns><c>true</c> if the subscription cancellation succeeds; otherwise <c>false</c>.</returns>
        public virtual bool Unsubscribe(TSubscriber subscriber, TClassifier classifier)
        {
            lock (_classifiers)
            {
                var res = false;

                if (_classifiers.TryGetValue(classifier, out var subscribers))
                {
                    if (subscribers.RemoveAll(s => s.Subscriber.Equals(subscriber)) > 0)
                        res = true;
                }
                else
                {
                    foreach (var kvp in _classifiers) {
                        if (!IsSubClassification(kvp.Key, classifier))
                            continue;

                        var subscriptions = kvp.Value.Where(ss => ss.Subscriber.Equals(subscriber)).ToList();
                        foreach (var existingSubscriber in subscriptions) {
                            existingSubscriber.Unsubscriptions.Add(classifier);
                            res = true;
                        }
                    }
                }

                ClearCache();
                return res;
            }
        }

        private void ClearCache()
        {
            _cache = new ConcurrentDictionary<TClassifier, List<TSubscriber>>();
        }

        /// <summary>
        /// Determines whether a specified classifier, <paramref name="child"/>, is a subclass of another classifier, <paramref name="parent"/>.
        /// </summary>
        /// <param name="parent">The potential parent of the classifier that is being checked.</param>
        /// <param name="child">The classifier that is being checked.</param>
        /// <returns><c>true</c> if the <paramref name="child"/> classifier is a subclass of <paramref name="parent"/>; otherwise <c>false</c>.</returns>
        protected abstract bool IsSubClassification(TClassifier parent, TClassifier child);

        /// <summary>
        /// Publishes the specified event directly to the specified subscriber.
        /// </summary>
        /// <param name="event">The event that is being published.</param>
        /// <param name="subscriber">The subscriber that receives the event.</param>
        protected abstract void Publish(TEvent @event, TSubscriber subscriber);

        /// <summary>
        /// Classifies the specified event using the specified classifier.
        /// </summary>
        /// <param name="event">The event that is being classified.</param>
        /// <param name="classifier">The classifier used to classify the event.</param>
        /// <returns><c>true</c> if the classification succeeds; otherwise <c>false</c>.</returns>
        protected abstract bool Classify(TEvent @event, TClassifier classifier);

        /// <summary>
        /// Retrieves the classifier used to classify the specified event.
        /// </summary>
        /// <param name="event">The event for which to retrieve the classifier.</param>
        /// <returns>The classifier used to classify the event.</returns>
        protected abstract TClassifier GetClassifier(TEvent @event);

        /// <summary>
        /// Publishes the specified event to the bus.
        /// </summary>
        /// <param name="event">The event that is being published.</param>
        public virtual void Publish(TEvent @event)
        {
            var eventClass = GetClassifier(@event);

            if (_cache.TryGetValue(eventClass, out var cachedSubscribers))
                PublishToSubscribers(@event, cachedSubscribers);
            else
            {
                cachedSubscribers = UpdateCacheForEventClassifier(@event, eventClass);
                PublishToSubscribers(@event, cachedSubscribers);
            }
        }

        private void PublishToSubscribers(TEvent @event, List<TSubscriber> cachedSubscribers)
        {
            foreach (var subscriber in cachedSubscribers)
            {
                Publish(@event, subscriber);
            }
        }

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
