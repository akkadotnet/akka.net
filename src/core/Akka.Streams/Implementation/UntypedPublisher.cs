//-----------------------------------------------------------------------
// <copyright file="UntypedPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams
{
    /// <summary>
    /// Publisher abstraction used where stream modules pass publishers without a compile-time element type.
    /// </summary>
    public interface IUntypedPublisher
    {
        /// <summary>
        /// Subscribes the untyped subscriber to this publisher.
        /// </summary>
        /// <param name="subscriber">The subscriber that receives this publisher's signals.</param>
        void Subscribe(IUntypedSubscriber subscriber);
    }

    /// <summary>
    /// Base adapter for exposing a typed Reactive Streams publisher through an untyped module boundary.
    /// </summary>
    internal abstract class UntypedPublisher : IUntypedPublisher
    {
        /// <summary>
        /// Subscribes the untyped subscriber to the wrapped publisher.
        /// </summary>
        /// <param name="subscriber">The subscriber that receives the publisher's signals.</param>
        public abstract void Subscribe(IUntypedSubscriber subscriber);

        /// <summary>
        /// Gets the wrapped typed publisher.
        /// </summary>
        /// <returns>The original typed publisher object.</returns>
        public abstract object Unwrap();

        /// <summary>
        /// Subscribes a subscriber that was handed over as a plain object (what a sink module creates)
        /// to the wrapped publisher, cast to the publisher's own element type.
        /// </summary>
        /// <param name="subscriber">An <see cref="ISubscriber{T}"/> of the wrapped publisher's element type.</param>
        public abstract void SubscribeTyped(object subscriber);

        /// <summary>
        /// Subscribes a <see cref="Implementation.CancellingSubscriber{T}"/> of the wrapped publisher's
        /// element type to it.
        /// </summary>
        public abstract void SubscribeCancellingSubscriber();

        /// <summary>
        /// Wraps a typed publisher so it can pass through an untyped stream-module boundary.
        /// </summary>
        /// <typeparam name="T">The publisher's element type.</typeparam>
        /// <param name="publisher">The publisher to wrap.</param>
        /// <returns>An adapter retaining the publisher's element type for subscription.</returns>
        public static UntypedPublisher FromTyped<T>(IPublisher<T> publisher)
        {
            return new UntypedPublisherImpl<T>(publisher);
        }

        /// <summary>
        /// Returns the original publisher when the supplied wrapper is an adapter, or the argument unchanged otherwise.
        /// </summary>
        /// <param name="untypedPublisher">The publisher to unwrap when it is an adapter.</param>
        /// <returns>The wrapped typed publisher, or <paramref name="untypedPublisher"/> unchanged.</returns>
        public static object ToTyped(IUntypedPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedPublisher publisher)
                return publisher.Unwrap();
            return untypedPublisher;
        }

        /// <summary>
        /// Converts an untyped publisher to a publisher with the requested element type.
        /// </summary>
        /// <typeparam name="T">The expected element type.</typeparam>
        /// <param name="untypedPublisher">The publisher to unwrap and cast.</param>
        /// <returns>The publisher cast to <see cref="IPublisher{T}"/>.</returns>
        public static IPublisher<T> ToTyped<T>(IUntypedPublisher untypedPublisher)
        {
            return (IPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    /// <summary>
    /// Adapter that forwards subscriptions to a typed publisher.
    /// </summary>
    /// <typeparam name="T">The publisher's element type.</typeparam>
    internal sealed class UntypedPublisherImpl<T> : UntypedPublisher
    {
        private readonly IPublisher<T> _publisher;

        /// <summary>
        /// Wraps the supplied typed publisher.
        /// </summary>
        /// <param name="publisher">The publisher to wrap.</param>
        public UntypedPublisherImpl(IPublisher<T> publisher)
        {
            _publisher = publisher;
        }

        /// <summary>
        /// Converts the untyped subscriber to the publisher's element type and subscribes it.
        /// </summary>
        /// <param name="subscriber">The subscriber to connect to the wrapped publisher.</param>
        public override void Subscribe(IUntypedSubscriber subscriber)
        {
            _publisher.Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));
        }

        /// <summary>
        /// Returns the wrapped publisher for a typed module boundary.
        /// </summary>
        /// <returns>The original publisher.</returns>
        public override object Unwrap()
        {
            return _publisher;
        }

        /// <inheritdoc/>
        public override void SubscribeTyped(object subscriber) => _publisher.Subscribe((ISubscriber<T>) subscriber);

        /// <inheritdoc/>
        public override void SubscribeCancellingSubscriber() => _publisher.Subscribe(new CancellingSubscriber<T>());

        /// <summary>
        /// Returns a string representation of the wrapped publisher.
        /// </summary>
        /// <returns>The wrapped publisher's string representation.</returns>
        public override string ToString()
        {
            return _publisher.ToString();
        }
    }
}
