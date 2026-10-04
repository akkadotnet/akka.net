//-----------------------------------------------------------------------
// <copyright file="SubscriberManagement.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Akka.Pattern;
using Akka.Streams.Actors;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Subscription associated with a subscriber and its read position in a shared stream buffer.
    /// </summary>
    /// <typeparam name="T">The type of elements delivered to the subscriber.</typeparam>
    internal interface ISubscriptionWithCursor<in T> : ISubscription, ICursor
    {
        /// <summary>
        /// Gets the subscriber receiving elements through this subscription.
        /// </summary>
        ISubscriber<T> Subscriber { get; }

        /// <summary>
        /// Dispatches one element to the subscriber.
        /// </summary>
        /// <param name="element">The element to deliver.</param>
        void Dispatch(T element);

        /// <summary>
        /// Gets or sets whether this subscription remains registered for delivery.
        /// </summary>
        bool IsActive { get; set; }

        /// <summary>
        ///  Do not increment directly, use <see cref="SubscriberManagement{T, TStreamBuffer}.MoreRequested"/> instead (it provides overflow protection)!
        /// </summary>
        long TotalDemand { get; set; } // number of requested but not yet dispatched elements
    }

    #region End of stream

    /// <summary>
    /// Tracks whether a publisher is active, completed, or completed with failure and dispatches terminal signals.
    /// </summary>
    internal static class SubscriberManagement
    {
        /// <summary>
            /// Applies this lifecycle state to a subscriber; nonterminal state implementations reject the call.
        /// </summary>
        public interface IEndOfStream
        {
            /// <summary>
            /// Applies a completed or failed terminal state to the subscriber.
            /// </summary>
            /// <typeparam name="T">The subscriber's element type.</typeparam>
            /// <param name="subscriber">The subscriber receiving the terminal signal.</param>
            void Apply<T>(ISubscriber<T> subscriber);
        }

        /// <summary>
        /// Represents a stream that has not yet reached a terminal state.
        /// </summary>
        public sealed class NotReached : IEndOfStream
        {
            /// <summary>
            /// Gets the singleton not-reached state.
            /// </summary>
            public static readonly NotReached Instance = new();
            private NotReached() { }

            /// <summary>
            /// Applying this state is an error because no terminal signal has been reached.
            /// </summary>
            /// <typeparam name="T">The subscriber's element type.</typeparam>
            /// <param name="subscriber">The subscriber that must not yet receive a terminal signal.</param>
            /// <exception cref="IllegalStateException">This state is not terminal and cannot be applied.</exception>
            public void Apply<T>(ISubscriber<T> subscriber)
            {
                throw new IllegalStateException("Called Apply on NotReached");
            }
        }

        /// <summary>
        /// Represents successful stream completion.
        /// </summary>
        public sealed class Completed : IEndOfStream
        {
            /// <summary>
            /// Gets the singleton successful-completion state.
            /// </summary>
            public static readonly Completed Instance = new();
            private Completed() { }

            /// <summary>
            /// Signals successful completion to the subscriber.
            /// </summary>
            /// <typeparam name="T">The subscriber's element type.</typeparam>
            /// <param name="subscriber">The subscriber receiving completion.</param>
            public void Apply<T>(ISubscriber<T> subscriber) => ReactiveStreamsCompliance.TryOnComplete(subscriber);
        }

        /// <summary>
        /// Represents stream completion with a failure cause.
        /// </summary>
        public sealed class ErrorCompleted : IEndOfStream
        {
            /// <summary>
            /// Gets the failure signaled to subscribers.
            /// </summary>
            public readonly Exception Cause;

            /// <summary>
            /// Creates a failed terminal state.
            /// </summary>
            /// <param name="cause">The failure to signal.</param>
            public ErrorCompleted(Exception cause)
            {
                Cause = cause;
            }

            /// <summary>
            /// Signals the stored failure to the subscriber.
            /// </summary>
            /// <typeparam name="T">The subscriber's element type.</typeparam>
            /// <param name="subscriber">The subscriber receiving the failure.</param>
            public void Apply<T>(ISubscriber<T> subscriber) => ReactiveStreamsCompliance.TryOnError(subscriber, Cause);
        }

        /// <summary>
        /// Gets the shared shutdown terminal state used when the last subscriber cancels.
        /// </summary>
        public static readonly IEndOfStream ShutDown = new ErrorCompleted(ActorPublisher.NormalShutdownReason);
    }

    #endregion

    /// <summary>
    /// Coordinates subscriptions, demand, a shared buffer, and terminal signals for a publisher or processor.
    /// </summary>
    /// <typeparam name="T">The type of elements shared with subscribers.</typeparam>
    /// <typeparam name="TStreamBuffer">The buffer implementation retaining values for registered cursors.</typeparam>
    internal abstract class SubscriberManagement<T, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TStreamBuffer> : ICursors where TStreamBuffer : IStreamBuffer<T>
    {
        private readonly Lazy<IStreamBuffer<T>> _buffer;

        // optimize for small numbers of subscribers by keeping subscribers in a plain list
        private ICollection<ISubscriptionWithCursor<T>> _subscriptions = new List<ISubscriptionWithCursor<T>>();

        // number of elements already requested but not yet received from upstream
        private long _pendingFromUpstream;

        // if non-null, holds the end-of-stream state
        private SubscriberManagement.IEndOfStream _endOfStream = SubscriberManagement.NotReached.Instance;

        /// <summary>
        /// Creates subscriber management and lazily initializes its shared stream buffer.
        /// </summary>
        protected SubscriberManagement()
        {
            // TStreamBuffer carries [DynamicallyAccessedMembers(PublicConstructors)], so the trimmer keeps
            // the (int, int, ICursors) constructor of every buffer type this is closed over.
            _buffer = new Lazy<IStreamBuffer<T>>(() 
                => (IStreamBuffer<T>) Activator.CreateInstance(typeof(TStreamBuffer), InitialBufferSize, MaxBufferSize, this));
        }

        /// <summary>
        /// Gets the initial capacity used when creating the stream buffer.
        /// </summary>
        public abstract int InitialBufferSize { get; }

        /// <summary>
        /// Gets the maximum capacity used when creating the stream buffer.
        /// </summary>
        public abstract int MaxBufferSize { get; }

        /// <summary>
        /// Gets the cursors for all currently registered subscriptions.
        /// </summary>
        public IEnumerable<ICursor> Cursors => _subscriptions;

        /// <summary>
        /// Called when we are ready to consume more elements from our upstream.
        /// MUST NOT call <see cref="PushToDownstream"/>.
        /// </summary>
        /// <param name="elements">The number of elements to request from upstream.</param>
        protected abstract void RequestFromUpstream(long elements);

        /// <summary>
        /// Called before <see cref="Shutdown"/> if the stream is *not* being regularly completed
        /// but shut-down due to the last subscriber having cancelled its subscription
        /// </summary>
        protected abstract void CancelUpstream();

        /// <summary>
        /// Called when the spi.Publisher/Processor is ready to be shut down.
        /// </summary>
        /// <param name="isCompleted">Whether shutdown follows successful upstream completion.</param>
        protected abstract void Shutdown(bool isCompleted);

        /// <summary>
        /// Use to register a subscriber
        /// </summary>
        /// <param name="subscriber">The subscriber to register.</param>
        /// <returns>A subscription associated with the subscriber and a cursor into the shared buffer.</returns>
        protected abstract ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber);

        /// <summary>
        /// More demand was signaled from a given subscriber.
        /// </summary>
        /// <param name="subscription">The active subscription requesting more elements.</param>
        /// <param name="elements">The requested element count; non-positive demand is signaled as a protocol error.</param>
        protected void MoreRequested(ISubscriptionWithCursor<T> subscription, long elements)
        {
            if (!subscription.IsActive) return;

            // check for illegal demand See 3.9
            if (elements < 1)
            {
                try
                {
                    ReactiveStreamsCompliance.TryOnError(subscription.Subscriber, ReactiveStreamsCompliance.NumberOfElementsInRequestMustBePositiveException);
                }
                finally
                {
                    UnregisterSubscriptionInternal(subscription);
                }
            }
            else
            {
                if (_endOfStream is SubscriberManagement.NotReached or SubscriberManagement.Completed)
                {
                    var d = subscription.TotalDemand + elements;
                    // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
                    var demand = d < 1 ? long.MaxValue : d;
                    subscription.TotalDemand = demand;
                    // returns Long.MinValue if the subscription is to be terminated
                    var remainingRequested = DispatchFromBufferAndReturnRemainingRequested(demand, subscription, _endOfStream);
                    if (remainingRequested == long.MinValue)
                    {
                        _endOfStream.Apply(subscription.Subscriber);
                        UnregisterSubscriptionInternal(subscription);
                    }
                    else
                    {
                        subscription.TotalDemand = remainingRequested;
                        RequestFromUpstreamIfRequired();
                    }
                }
            }
        }

        private long DispatchFromBufferAndReturnRemainingRequested(long requested, ISubscriptionWithCursor<T> subscription, SubscriberManagement.IEndOfStream endOfStream)
        {
            while (requested != 0)
            {
                if (_buffer.Value.Count(subscription) > 0)
                {
                    bool goOn;
                    try
                    {
                        subscription.Dispatch(_buffer.Value.Read(subscription));
                        goOn = true;
                    }
                    catch (Exception e)
                        when (e is ISpecViolation)
                    {
                        UnregisterSubscriptionInternal(subscription);
                        goOn = false;
                    }

                    if (!goOn)
                        return long.MinValue;

                    requested--;
                }
                else if (!(endOfStream is SubscriberManagement.NotReached))
                    return long.MinValue;
                else
                    return requested;
            }

            // if request == 0
            // if we are at end-of-stream and have nothing more to read we complete now rather than after the next requestMore
            return !(endOfStream is SubscriberManagement.NotReached) && _buffer.Value.Count(subscription) == 0 ? long.MinValue : 0;
        }

        private void RequestFromUpstreamIfRequired()
        {
            var maxRequested = _subscriptions.Select(x => x.TotalDemand).Max();
            var desired =
                (int) Math.Min(int.MaxValue, Math.Min(maxRequested, _buffer.Value.CapacityLeft) - _pendingFromUpstream);
            if (desired > 0)
            {
                _pendingFromUpstream += desired;
                RequestFromUpstream(desired);
            }
        }

        /// <summary>
        /// This method must be called by the implementing class whenever a new value is available to be pushed downstream.
        /// </summary>
        /// <param name="value">The value received from upstream and retained for active subscribers.</param>
        /// <exception cref="IllegalStateException">The buffer cannot accept the value or the stream has already terminated.</exception>
        protected void PushToDownstream(T value)
        {
            if (_endOfStream is SubscriberManagement.NotReached)
            {
                _pendingFromUpstream--;
                if (!_buffer.Value.Write(value))
                    throw new IllegalStateException("Output buffer overflow");
                if (_buffer.Value.AvailableData > 0 && Dispatch(_subscriptions))
                    RequestFromUpstreamIfRequired();
            }
            else throw new IllegalStateException("PushToDownStream(...) after CompleteDownstream() or AbortDownstream(...)");
        }

        private bool Dispatch(ICollection<ISubscriptionWithCursor<T>> subscriptions)
        {
            var wasSend = false;

            foreach (var subscription in subscriptions)
            {
                if (subscription.TotalDemand > 0)
                {
                    var element = _buffer.Value.Read(subscription);
                    subscription.Dispatch(element);
                    subscription.TotalDemand--;
                    wasSend = true;
                }
            }

            return wasSend;
        }

        /// <summary>
        /// This method must be called by the implementing class whenever
        /// it has been determined that no more elements will be produced
        /// </summary>
        protected void CompleteDownstream()
        {
            if (_endOfStream is SubscriberManagement.NotReached)
            {
                _endOfStream = SubscriberManagement.Completed.Instance;
                _subscriptions = CompleteDoneSubscriptions(_subscriptions);
                if (_subscriptions.Count == 0)
                    Shutdown(true);
            }
            // else ignore, we need to be idempotent
        }

        private ICollection<ISubscriptionWithCursor<T>> CompleteDoneSubscriptions(ICollection<ISubscriptionWithCursor<T>> subscriptions)
        {
            var result = new List<ISubscriptionWithCursor<T>>();
            foreach (var subscription in subscriptions)
            {
                if (_buffer.Value.Count(subscription) == 0)
                {
                    subscription.IsActive = false;
                    SubscriberManagement.Completed.Instance.Apply(subscription.Subscriber);
                }
                else
                    result.Add(subscription);
            }
            return result;
        }

        /// <summary>
        /// This method must be called by the implementing class to push an error downstream.
        /// </summary>
        /// <param name="cause">The failure signaled to registered subscribers.</param>
        protected void AbortDownstream(Exception cause)
        {
            _endOfStream = new SubscriberManagement.ErrorCompleted(cause);
            foreach (var subscription in _subscriptions)
                _endOfStream.Apply(subscription.Subscriber);
            _subscriptions.Clear();
        }

        /// <summary>
        /// Register a new subscriber.
        /// </summary>
        /// <param name="subscriber">The subscriber to register or notify of the existing terminal state.</param>
        protected void RegisterSubscriber(ISubscriber<T> subscriber)
        {
            if (_endOfStream is SubscriberManagement.NotReached)
                if (_subscriptions.Any(s => s.Subscriber.Equals(subscriber)))
                    ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "SubscriberManagement");
                else
                    AddSubscription(subscriber);
            else if (_endOfStream is SubscriberManagement.Completed && !_buffer.Value.IsEmpty)
                AddSubscription(subscriber);
            else _endOfStream.Apply(subscriber);
        }

        private void AddSubscription(ISubscriber<T> subscriber)
        {
            var newSubscription = CreateSubscription(subscriber);
            _subscriptions.Add(newSubscription);
            _buffer.Value.InitCursor(newSubscription);
            try
            {
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, newSubscription);
            }
            catch (Exception e)
            {
                if (e is ISpecViolation)
                    UnregisterSubscriptionInternal(newSubscription);
                else throw;
            }
        }

        /// <summary>
        /// Called from <see cref="ISubscription.Cancel"/>, i.e. from another thread,
        /// override to add synchronization with itself, <see cref="Subscribe{T}"/> and <see cref="MoreRequested"/>
        /// </summary>
        /// <param name="subscription">The subscription to remove from the active reader set.</param>
        protected void UnregisterSubscription(ISubscriptionWithCursor<T> subscription)
            => UnregisterSubscriptionInternal(subscription);

        // must be idempotent
        private void UnregisterSubscriptionInternal(ISubscriptionWithCursor<T> subscription)
        {
            if (subscription.IsActive)
            {
                _subscriptions.Remove(subscription);
                _buffer.Value.OnCursorRemoved(subscription);
                subscription.IsActive = false;
                if (_subscriptions.Count == 0)
                {
                    if (_endOfStream is SubscriberManagement.NotReached)
                    {
                        _endOfStream = SubscriberManagement.ShutDown;
                        CancelUpstream();
                    }

                    Shutdown(false);
                }
                else RequestFromUpstreamIfRequired(); // we might have removed a "blocking" subscriber and can continue now
            }
            // else ignore, we need to be idempotent
        }
    }
}
