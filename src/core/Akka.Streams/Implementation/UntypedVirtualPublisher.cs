//-----------------------------------------------------------------------
// <copyright file="UntypedVirtualPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Implementation;

namespace Akka.Streams
{
    /// <summary>
    /// Publisher facade whose backing publisher can be registered after this object has been exposed to a module.
    /// </summary>
    internal interface IUntypedVirtualPublisher
    {
        /// <summary>
        /// Connects the subscriber when a publisher is available, including when registration occurs later.
        /// </summary>
        /// <param name="subscriber">The subscriber to connect.</param>
        void Subscribe(IUntypedSubscriber subscriber);
        /// <summary>
        /// Registers the publisher that will receive subscribers of this facade.
        /// </summary>
        /// <param name="publisher">The publisher to register.</param>
        void RegisterPublisher(IUntypedPublisher publisher);
        /// <summary>
        /// Registers an <see cref="ErrorPublisher{T}"/> of this publisher's element type, failing with
        /// <paramref name="cause"/>.
        /// </summary>
        /// <param name="cause">The failure to signal.</param>
        void RegisterErrorPublisher(Exception cause);
    }

    /// <summary>
    /// Base adapter for exposing a typed virtual publisher through an untyped module boundary.
    /// </summary>
    internal abstract class UntypedVirtualPublisher : IUntypedVirtualPublisher
    {
        /// <summary>
        /// Connects the subscriber when a publisher is available, including when registration occurs later.
        /// </summary>
        /// <param name="subscriber">The subscriber to connect.</param>
        public abstract void Subscribe(IUntypedSubscriber subscriber);
        /// <summary>
        /// Registers the publisher to which this virtual publisher forwards subscribers.
        /// </summary>
        /// <param name="publisher">The publisher to register.</param>
        public abstract void RegisterPublisher(IUntypedPublisher publisher);

        /// <inheritdoc/>
        public abstract void RegisterErrorPublisher(Exception cause);

        /// <summary>
        /// Gets the wrapped virtual publisher.
        /// </summary>
        /// <returns>The original typed virtual publisher.</returns>
        public abstract object Unwrap();

        /// <summary>
        /// Wraps a typed virtual publisher for an untyped module boundary.
        /// </summary>
        /// <typeparam name="T">The publisher's element type.</typeparam>
        /// <param name="publisher">The publisher to wrap.</param>
        /// <returns>An adapter that preserves its element type.</returns>
        public static UntypedVirtualPublisher FromTyped<T>(VirtualPublisher<T> publisher)
        {
            return new UntypedVirtualPublisherImpl<T>(publisher);
        }

        /// <summary>
        /// Returns the wrapped typed publisher for adapters, or passes through a non-adapter implementation.
        /// </summary>
        /// <param name="untypedPublisher">The publisher to unwrap when it is an adapter.</param>
        /// <returns>The wrapped publisher, or <paramref name="untypedPublisher"/> unchanged.</returns>
        public static object ToTyped(IUntypedVirtualPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedVirtualPublisher publisher)
                return publisher.Unwrap();
            return untypedPublisher;
        }

        /// <summary>
        /// Converts an untyped virtual publisher to the requested element type.
        /// </summary>
        /// <typeparam name="T">The expected element type.</typeparam>
        /// <param name="untypedPublisher">The publisher to unwrap and cast.</param>
        /// <returns>The publisher cast to <see cref="VirtualPublisher{T}"/>.</returns>
        public static VirtualPublisher<T> ToTyped<T>(IUntypedVirtualPublisher untypedPublisher)
        {
            return (VirtualPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    /// <summary>
    /// Adapter that forwards subscriptions and publisher registration to a typed virtual publisher.
    /// </summary>
    /// <typeparam name="T">The publisher's element type.</typeparam>
    internal sealed class UntypedVirtualPublisherImpl<T> : UntypedVirtualPublisher
    {
        private readonly VirtualPublisher<T> _publisher;

        /// <summary>
        /// Wraps the supplied virtual publisher.
        /// </summary>
        /// <param name="publisher">The virtual publisher to wrap.</param>
        public UntypedVirtualPublisherImpl(VirtualPublisher<T> publisher)
        {
            _publisher = publisher;
        }

        /// <summary>
        /// Converts the subscriber to the publisher's element type and subscribes it.
        /// </summary>
        /// <param name="subscriber">The subscriber to connect.</param>
        public override void Subscribe(IUntypedSubscriber subscriber)
        {
            _publisher.Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));
        }

        /// <summary>
        /// Converts and registers the typed publisher with the wrapped virtual publisher.
        /// </summary>
        /// <param name="publisher">The publisher to register.</param>
        public override void RegisterPublisher(IUntypedPublisher publisher)
        {
            _publisher.RegisterPublisher(UntypedPublisher.ToTyped<T>(publisher));
        }

        /// <inheritdoc/>
        public override void RegisterErrorPublisher(Exception cause) => ((IUntypedVirtualPublisher)_publisher).RegisterErrorPublisher(cause);

        /// <summary>
        /// Returns the wrapped publisher for a typed module boundary.
        /// </summary>
        /// <returns>The original virtual publisher.</returns>
        public override object Unwrap() => _publisher;

        /// <summary>
        /// Returns a string representation of the wrapped publisher.
        /// </summary>
        /// <returns>The wrapped publisher's string representation.</returns>
        public override string ToString() => _publisher.ToString();
    }
}
