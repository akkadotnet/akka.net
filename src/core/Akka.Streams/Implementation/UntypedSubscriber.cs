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
    /// Subscriber abstraction used where stream modules pass subscribers without a compile-time element type.
    /// </summary>
    public interface IUntypedSubscriber
    {
        /// <summary>
        /// Receives the subscription supplied by the publisher.
        /// </summary>
        /// <param name="subscription">The subscription used to request elements or cancel.</param>
        void OnSubscribe(ISubscription subscription);
        /// <summary>
        /// Receives the next element from the publisher.
        /// </summary>
        /// <param name="element">The element delivered by the publisher.</param>
        void OnNext(object element);
        /// <summary>
        /// Receives terminal failure from the publisher.
        /// </summary>
        /// <param name="cause">The failure signaled by the publisher.</param>
        void OnError(Exception cause);
        /// <summary>
        /// Receives successful completion from the publisher.
        /// </summary>
        void OnComplete();
    }

    /// <summary>
    /// Base adapter for exposing a typed Reactive Streams subscriber through an untyped module boundary.
    /// </summary>
    internal abstract class UntypedSubscriber : IUntypedSubscriber
    {
        /// <summary>
        /// Forwards the subscription to the wrapped subscriber.
        /// </summary>
        /// <param name="subscription">The subscription to forward.</param>
        public abstract void OnSubscribe(ISubscription subscription);

        /// <summary>
        /// Casts and forwards the element to the wrapped subscriber.
        /// </summary>
        /// <param name="element">The element to cast to the wrapped subscriber's type.</param>
        public abstract void OnNext(object element);

        /// <summary>
        /// Forwards the failure to the wrapped subscriber.
        /// </summary>
        /// <param name="cause">The failure to forward.</param>
        public abstract void OnError(Exception cause);

        /// <summary>
        /// Forwards successful completion to the wrapped subscriber.
        /// </summary>
        public abstract void OnComplete();

        /// <summary>
        /// Gets the wrapped typed subscriber.
        /// </summary>
        /// <returns>The original typed subscriber object.</returns>
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
        /// Wraps a typed subscriber so it can pass through an untyped stream-module boundary.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber to wrap.</param>
        /// <returns>An adapter that casts incoming elements to <typeparamref name="T"/>.</returns>
        public static UntypedSubscriber FromTyped<T>(ISubscriber<T> subscriber)
        {
            return new UntypedSubscriberImpl<T>(subscriber);
        }

        /// <summary>
        /// Returns the original subscriber when the supplied wrapper is an adapter, or the argument unchanged otherwise.
        /// </summary>
        /// <param name="untypedSubscriber">The subscriber to unwrap when it is an adapter.</param>
        /// <returns>The wrapped typed subscriber, or <paramref name="untypedSubscriber"/> unchanged.</returns>
        public static object ToTyped(IUntypedSubscriber untypedSubscriber)
        {
            if (untypedSubscriber is UntypedSubscriber subscriber)
                return subscriber.Unwrap();
            return untypedSubscriber;
        }

        /// <summary>
        /// Converts an untyped subscriber to a subscriber with the requested element type.
        /// </summary>
        /// <typeparam name="T">The expected element type.</typeparam>
        /// <param name="untypedSubscriber">The subscriber to unwrap and cast.</param>
        /// <returns>The subscriber cast to <see cref="ISubscriber{T}"/>.</returns>
        public static ISubscriber<T> ToTyped<T>(IUntypedSubscriber untypedSubscriber)
        {
            return (ISubscriber<T>) ToTyped(untypedSubscriber);
        }
    }

    /// <summary>
    /// Adapter that forwards Reactive Streams callbacks to a typed subscriber.
    /// </summary>
    /// <typeparam name="T">The subscriber's element type.</typeparam>
    internal sealed class UntypedSubscriberImpl<T> : UntypedSubscriber
    {
        private readonly ISubscriber<T> _subscriber;

        /// <summary>
        /// Wraps the supplied typed subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber to wrap.</param>
        public UntypedSubscriberImpl(ISubscriber<T> subscriber)
        {
            _subscriber = subscriber;
        }

        /// <summary>
        /// Forwards the subscription to the wrapped subscriber.
        /// </summary>
        /// <param name="subscription">The subscription to forward.</param>
        public override void OnSubscribe(ISubscription subscription)
        {
            _subscriber.OnSubscribe(subscription);
        }

        /// <summary>
        /// Casts and forwards the element to the wrapped subscriber.
        /// </summary>
        /// <param name="element">The element to cast to <typeparamref name="T"/> and forward.</param>
        public override void OnNext(object element)
        {
            _subscriber.OnNext((T) element);
        }

        /// <summary>
        /// Forwards the failure to the wrapped subscriber.
        /// </summary>
        /// <param name="cause">The failure to forward.</param>
        public override void OnError(Exception cause)
        {
            _subscriber.OnError(cause);
        }

        /// <summary>
        /// Forwards successful completion to the wrapped subscriber.
        /// </summary>
        public override void OnComplete()
        {
            _subscriber.OnComplete();
        }

        /// <summary>
        /// Returns the wrapped subscriber for a typed module boundary.
        /// </summary>
        /// <returns>The original subscriber.</returns>
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
        /// Returns a string representation of the wrapped subscriber.
        /// </summary>
        /// <returns>The wrapped subscriber's string representation.</returns>
        public override string ToString()
        {
            return _subscriber.ToString();
        }
    }
}
