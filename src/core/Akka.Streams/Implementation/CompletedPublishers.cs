//-----------------------------------------------------------------------
// <copyright file="CompletedPublishers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Publisher that completes each subscriber without emitting elements.
    /// </summary>
    /// <typeparam name="T">The type of elements the publisher could emit.</typeparam>
    public sealed class EmptyPublisher<T> : IPublisher<T>
    {
        /// <summary>
        /// The shared empty publisher instance.
        /// </summary>
        public static readonly IPublisher<T> Instance = new EmptyPublisher<T>();

        private EmptyPublisher() { }

        /// <summary>
        /// Subscribes the consumer and immediately signals completion.
        /// </summary>
        /// <param name="subscriber">The subscriber to notify.</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnComplete(subscriber);
            }
            catch (Exception e)
                when (e is ISpecViolation)
            {
            }
        }

        /// <summary>
        /// Returns the diagnostic name of this publisher.
        /// </summary>
        /// <returns>The string <c>already-completed-publisher</c>.</returns>
        public override string ToString() => "already-completed-publisher";
    }

    /// <summary>
    /// Publisher that signals a stored failure to each subscriber.
    /// </summary>
    /// <typeparam name="T">The type of elements the publisher would otherwise emit.</typeparam>
    internal sealed class ErrorPublisher<T> : IPublisher<T>
    {
        /// <summary>
        /// The diagnostic name returned by <see cref="ToString"/>.
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// The failure signaled to subscribers.
        /// </summary>
        public readonly Exception Cause;

        /// <summary>
        /// Creates a publisher that reports the specified failure.
        /// </summary>
        /// <param name="cause">The failure signaled to subscribers.</param>
        /// <param name="name">The diagnostic name of the publisher.</param>
        public ErrorPublisher(Exception cause, string name)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(cause);
            Cause = cause;
            Name = name;
        }

        /// <summary>
        /// Rejects the subscriber according to Reactive Streams rules.
        /// </summary>
        /// <param name="subscriber">The subscriber to reject.</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnError(subscriber, Cause);
            }
            catch (Exception e)
                when (e is ISpecViolation)
            {
            }
        }

        /// <summary>
        /// Returns the diagnostic name of this publisher.
        /// </summary>
        /// <returns>The name supplied when this publisher was created.</returns>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Publisher that emits the result of a promise when requested, or completes without an element for the default value.
    /// </summary>
    /// <typeparam name="T">The type of the optional element produced by the promise.</typeparam>
    internal sealed class MaybePublisher<T> : IPublisher<T>
    {
        private class MaybeSubscription : ISubscription
        {
            private readonly ISubscriber<T> _subscriber;
            private readonly TaskCompletionSource<T> _promise;
            private bool _done;

            public MaybeSubscription(ISubscriber<T> subscriber, TaskCompletionSource<T> promise)
            {
                _subscriber = subscriber;
                _promise = promise;
            }

            public void Request(long n)
            {
                if (n < 1)
                    ReactiveStreamsCompliance.RejectDueToNonPositiveDemand(_subscriber);
                if (!_done)
                {
                    _done = true;
                    _promise.Task.ContinueWith(_ =>
                    {
                        if (!_promise.Task.Result.IsDefaultForType())
                        {
                            ReactiveStreamsCompliance.TryOnNext(_subscriber, _promise.Task.Result);
                            ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                        }
                        else
                            ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                    }, TaskContinuationOptions.OnlyOnRanToCompletion);
                }
            }

            public void Cancel()
            {
                _done = true;
                _promise.TrySetResult(default(T));
            }
        }

        /// <summary>
        /// The promise whose nondefault successful result can be emitted to subscribers.
        /// </summary>
        public readonly TaskCompletionSource<T> Promise;
        /// <summary>
        /// The diagnostic name returned by <see cref="ToString"/>.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// Creates a publisher backed by a promise.
        /// </summary>
        /// <param name="promise">The promise whose nondefault result or failure is signaled to subscribers; a default result completes without an element.</param>
        /// <param name="name">The diagnostic name of the publisher.</param>
        public MaybePublisher(TaskCompletionSource<T> promise, string name)
        {
            Promise = promise;
            Name = name;
        }

        /// <summary>
        /// Subscribes the consumer, emitting a nondefault successful result after demand, completing without an element for a default result, or reporting the promise failure.
        /// </summary>
        /// <param name="subscriber">The subscriber to notify.</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
            ReactiveStreamsCompliance.TryOnSubscribe(subscriber, new MaybeSubscription(subscriber, Promise));
            Promise.Task.ContinueWith(t =>
            {
                ReactiveStreamsCompliance.TryOnError(subscriber, t.Exception);
            }, TaskContinuationOptions.NotOnRanToCompletion);
        }

        /// <summary>
        /// Returns the diagnostic name of this publisher.
        /// </summary>
        /// <returns>The name supplied when this publisher was created.</returns>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Subscription whose request and cancel operations are no-ops.
    /// </summary>
    internal sealed class CancelledSubscription : ISubscription
    {
        /// <summary>
        /// The shared cancelled subscription instance.
        /// </summary>
        public static readonly CancelledSubscription Instance = new();

        private CancelledSubscription() { }

        /// <summary>
        /// Ignores a request because this subscription is already cancelled.
        /// </summary>
        /// <param name="n">The requested number of elements.</param>
        public void Request(long n) { }

        /// <summary>
        /// Does nothing because this subscription is already cancelled.
        /// </summary>
        public void Cancel() { }
    }

    /// <summary>
    /// Subscriber that cancels its subscription and ignores subsequent signals.
    /// </summary>
    /// <typeparam name="T">The type of elements the subscriber ignores.</typeparam>
    internal sealed class CancellingSubscriber<T> : ISubscriber<T>
    {
        /// <summary>
        /// Cancels the supplied subscription.
        /// </summary>
        /// <param name="subscription">The subscription to cancel.</param>
        public void OnSubscribe(ISubscription subscription) => subscription.Cancel();
        /// <summary>
        /// Ignores the received element after cancellation.
        /// </summary>
        /// <param name="element">The element received after cancellation.</param>
        public void OnNext(T element) { }
        /// <summary>
        /// Ignores an object-form element received after cancellation.
        /// </summary>
        /// <param name="element">The object received after cancellation.</param>
        public void OnNext(object element) { }
        /// <summary>
        /// Ignores the received failure after cancellation.
        /// </summary>
        /// <param name="cause">The failure received after cancellation.</param>
        public void OnError(Exception cause) { }
        /// <summary>
        /// Ignores completion after cancellation.
        /// </summary>
        public void OnComplete() { }
    }

    /// <summary>
    /// Publisher that rejects every subscription request as an additional subscriber.
    /// </summary>
    /// <typeparam name="T">The type of elements the publisher would otherwise emit.</typeparam>
    internal sealed class RejectAdditionalSubscribers<T> : IPublisher<T>
    {
        /// <summary>
        /// The shared publisher instance that rejects subscribers.
        /// </summary>
        public static readonly IPublisher<T> Instance = new RejectAdditionalSubscribers<T>();

        private RejectAdditionalSubscribers() { }

        /// <summary>
        /// Rejects the subscriber according to Reactive Streams rules.
        /// </summary>
        /// <param name="subscriber">The subscriber to reject.</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Publisher");
            }
            catch (Exception e)
                when (e is ISpecViolation)
            {
            }
        }

        /// <summary>
        /// Returns the diagnostic name of this publisher.
        /// </summary>
        /// <returns>The string <c>already-subscribed-publisher</c>.</returns>
        public override string ToString() => "already-subscribed-publisher";
    }
}
