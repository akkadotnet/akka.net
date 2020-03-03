//-----------------------------------------------------------------------
// <copyright file="UntypedSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IUntypedSubscriber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        void OnSubscribe(ISubscription subscription);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        void OnNext(object element);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        void OnError(Exception cause);
        /// <summary>
        /// TBD
        /// </summary>
        void OnComplete();
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class UntypedSubscriber : IUntypedSubscriber
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public abstract void OnSubscribe(ISubscription subscription);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public abstract void OnNext(object element);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public abstract void OnError(Exception cause);

        /// <summary>
        /// TBD
        /// </summary>
        public abstract void OnComplete();

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract object Unwrap();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        /// <returns>TBD</returns>
        public static UntypedSubscriber FromTyped(object subscriber)
        {
            var subscribedType = subscriber.GetType().GetSubscribedType();
            return (UntypedSubscriber) typeof(UntypedSubscriberImpl<>).Instantiate(subscribedType, subscriber);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="subscriber">TBD</param>
        /// <returns>TBD</returns>
        public static UntypedSubscriber FromTyped<T>(ISubscriber<T> subscriber)
        {
            return new UntypedSubscriberImpl<T>(subscriber);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="untypedSubscriber">TBD</param>
        /// <returns>TBD</returns>
        public static object ToTyped(IUntypedSubscriber untypedSubscriber)
        {
            if (untypedSubscriber is UntypedSubscriber)
                return ((UntypedSubscriber) untypedSubscriber).Unwrap();
            return untypedSubscriber;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="untypedSubscriber">TBD</param>
        /// <returns>TBD</returns>
        public static ISubscriber<T> ToTyped<T>(IUntypedSubscriber untypedSubscriber)
        {
            return (ISubscriber<T>) ToTyped(untypedSubscriber);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class UntypedSubscriberImpl<T> : UntypedSubscriber
    {
        private readonly ISubscriber<T> _subscriber;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public UntypedSubscriberImpl(ISubscriber<T> subscriber)
        {
            _subscriber = subscriber;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public override void OnSubscribe(ISubscription subscription)
        {
            _subscriber.OnSubscribe(subscription);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public override void OnNext(object element)
        {
            _subscriber.OnNext((T) element);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public override void OnError(Exception cause)
        {
            _subscriber.OnError(cause);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override void OnComplete()
        {
            _subscriber.OnComplete();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override object Unwrap()
        {
            return _subscriber;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return _subscriber.ToString();
        }
    }
}
