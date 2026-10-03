//-----------------------------------------------------------------------
// <copyright file="UntypedSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Streams.Implementation;
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
        /// Subscribes the wrapped subscriber to an <see cref="Implementation.ErrorPublisher{T}"/> failing
        /// with <paramref name="cause"/>, typed by the element type this wrapper was built with.
        /// </summary>
        /// <param name="cause">The failure to signal.</param>
        public abstract void SubscribeToErrorPublisher(Exception cause);

        /// <summary>
        /// Creates the <see cref="ActorSubscription{T}"/> that connects the wrapped subscriber to
        /// <paramref name="implementor"/>.
        /// </summary>
        /// <param name="implementor">The actor behind the publisher.</param>
        /// <returns>The subscription.</returns>
        public abstract IActorSubscription CreateActorSubscription(IActorRef implementor);

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
            if (untypedSubscriber is UntypedSubscriber subscriber)
                return subscriber.Unwrap();
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

        /// <inheritdoc/>
        public override void SubscribeToErrorPublisher(Exception cause)
            => new ErrorPublisher<T>(cause, string.Empty).Subscribe(_subscriber);

        /// <inheritdoc/>
        public override IActorSubscription CreateActorSubscription(IActorRef implementor)
            => new ActorSubscription<T>(implementor, _subscriber);

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
