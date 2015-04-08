//-----------------------------------------------------------------------
// <copyright file="Subscription.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;

namespace Akka.Event
{
    /// <summary>
    /// Class Subscription.
    /// </summary>
    /// <typeparam name="TSubscriber">The type of the t subscriber.</typeparam>
    /// <typeparam name="TClassifier">The type of the t classifier.</typeparam>
    public class Subscription<TSubscriber, TClassifier>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Subscription{TSubscriber, TClassifier}"/> class.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        /// <param name="unsubscriptions">The unsubscriptions.</param>
        public Subscription(TSubscriber subscriber, IEnumerable<TClassifier> unsubscriptions)
        {
            Subscriber = subscriber;
            Unsubscriptions = new HashSet<TClassifier>(unsubscriptions);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Subscription{TSubscriber, TClassifier}"/> class.
        /// </summary>
        /// <param name="subscriber">The subscriber.</param>
        public Subscription(TSubscriber subscriber)
        {
            Subscriber = subscriber;
            Unsubscriptions = new HashSet<TClassifier>();
        }

        /// <summary>
        /// Gets the subscriber.
        /// </summary>
        /// <value>The subscriber.</value>
        public TSubscriber Subscriber { get; private set; }
        /// <summary>
        /// Gets the unsubscriptions.
        /// </summary>
        /// <value>The unsubscriptions.</value>
        public ISet<TClassifier> Unsubscriptions { get; private set; }
    }
}

