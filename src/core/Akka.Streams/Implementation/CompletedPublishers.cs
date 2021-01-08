//-----------------------------------------------------------------------
// <copyright file="CompletedPublishers.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    public sealed class EmptyPublisher<T> : IPublisher<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly IPublisher<T> Instance = new EmptyPublisher<T>();

        private EmptyPublisher() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnComplete(subscriber);
            }
            catch (Exception e)
            {
                if (!(e is ISpecViolation))
                    throw;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "already-completed-publisher";
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class ErrorPublisher<T> : IPublisher<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Name;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly Exception Cause;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        /// <param name="name">TBD</param>
        public ErrorPublisher(Exception cause, string name)
        {
            ReactiveStreamsCompliance.RequireNonNullElement(cause);
            Cause = cause;
            Name = name;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RequireNonNullSubscriber(subscriber);
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnError(subscriber, Cause);
            }
            catch (Exception e)
            {
                if (!(e is ISpecViolation))
                    throw;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => Name;
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
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
                    _promise.Task.ContinueWith(t =>
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
        /// TBD
        /// </summary>
        public readonly TaskCompletionSource<T> Promise;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="promise">TBD</param>
        /// <param name="name">TBD</param>
        public MaybePublisher(TaskCompletionSource<T> promise, string name)
        {
            Promise = promise;
            Name = name;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
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
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => Name;
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal sealed class CancelledSubscription : ISubscription
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly CancelledSubscription Instance = new CancelledSubscription();

        private CancelledSubscription() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="n">TBD</param>
        public void Request(long n) { }

        /// <summary>
        /// TBD
        /// </summary>
        public void Cancel() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class CancellingSubscriber<T> : ISubscriber<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        public void OnSubscribe(ISubscription subscription) => subscription.Cancel();
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(T element) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="element">TBD</param>
        public void OnNext(object element) { }
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="cause">TBD</param>
        public void OnError(Exception cause) { }
        /// <summary>
        /// TBD
        /// </summary>
        public void OnComplete() { }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class RejectAdditionalSubscribers<T> : IPublisher<T>
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly IPublisher<T> Instance = new RejectAdditionalSubscribers<T>();

        private RejectAdditionalSubscribers() { }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            try
            {
                ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "Publisher");
            }
            catch (Exception e)
            {
                if (!(e is ISpecViolation))
                    throw;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => "already-subscribed-publisher";
    }
}
