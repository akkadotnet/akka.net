using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Streams;
using System.Runtime.Serialization;
using Akka.Pattern;
using Akka.Streams.Actors;

namespace Akka.Streams.Implementation
{
    internal interface ISubscriptionWithCursor<T> : ISubscription, ICursor
    {
        ISubscriber<T> Subscriber { get; }

        void Dispatch(T element);

        bool IsActive { get; set; }

        int Cursor { get; }     // buffer cursor, managed by buffer

        /// <summary>
        ///  Do not increment directly, use <see cref="SubscriberManagement{T}.MoreRequested"/> instead (it provides overflow protection)!
        /// </summary>
        long TotalDemand { get; set; } // number of requested but not yet dispatched elements
    }

    internal abstract class SubscriberManagement<T> : ICursors
    {
        #region End of stream

        public interface IEndOfStream
        {
            void Apply(ISubscriber<T> subscriber);
        }

        public sealed class NotReached : IEndOfStream
        {
            public static readonly NotReached Instance = new NotReached();
            private NotReached() { }

            public void Apply(ISubscriber<T> subscriber)
            {
                throw new IllegalStateException("Called Apply on NotReached");
            }
        }

        public sealed class Completed : IEndOfStream
        {
            public static readonly Completed Instance = new Completed();
            private Completed() { }

            public void Apply(ISubscriber<T> subscriber)
            {
                ReactiveStreamsCompliance.TryOnComplete(subscriber);
            }
        }

        public struct ErrorCompleted : IEndOfStream
        {
            public readonly Exception Cause;

            public ErrorCompleted(Exception cause)
                : this()
            {
                Cause = cause;
            }

            public void Apply(ISubscriber<T> subscriber)
            {
                ReactiveStreamsCompliance.TryOnError(subscriber, Cause);
            }
        }

        #endregion

        private static readonly IEndOfStream ShutDown = new ErrorCompleted(ActorPublisher.NormalShutdownReason);

        private readonly Lazy<ResizableMultiReaderRingBuffer<T>> _buffer;

        // optimize for small numbers of subscribers by keeping subscribers in a plain list
        private ICollection<ISubscriptionWithCursor<T>> _subscriptions = new List<ISubscriptionWithCursor<T>>(0);

        // number of elements already requested but not yet received from upstream
        private long _pendingFromUpstream = 0L;

        // if non-null, holds the end-of-stream state
        private IEndOfStream _endOfStream = NotReached.Instance;

        protected SubscriberManagement()
        {
            _buffer = new Lazy<ResizableMultiReaderRingBuffer<T>>(() =>
                new ResizableMultiReaderRingBuffer<T>(InitialBufferSize, MaxBufferSize, this));
        }

        public abstract int InitialBufferSize { get; }
        public abstract int MaxBufferSize { get; }
        public IEnumerable<ICursor> Cursors { get { return _subscriptions; } }

        /// <summary>
        /// Called when we are ready to consume more elements from our upstream.
        /// MUST NOT call <see cref="PushToDownstream"/>.
        /// </summary>
        protected abstract void RequestFromUpstream(long elements);

        /// <summary>
        /// Called before <see cref="Shutdown"/> if the stream is *not* being regularly completed
        /// but shut-down due to the last subscriber having cancelled its subscription
        /// </summary>
        protected abstract void CancelUpstream();

        /// <summary>
        /// Called when the spi.Publisher/Processor is ready to be shut down.
        /// </summary>
        protected abstract void Shutdown(bool isCompleted);

        /// <summary>
        /// Use to register a subscriber
        /// </summary>
        protected abstract ISubscriptionWithCursor<T> CreateSubscription(ISubscriber<T> subscriber);

        /// <summary>
        /// More demand was signaled from a given subscriber.
        /// </summary>
        protected void MoreRequested(ISubscriptionWithCursor<T> subscription, long elements)
        {
            if (subscription.IsActive)
            {
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
                    if (_endOfStream is NotReached || _endOfStream is Completed)
                    {
                        var d = subscription.TotalDemand + elements;
                        // Long overflow, Reactive Streams Spec 3:17: effectively unbounded
                        var demand = d < 1 ? long.MaxValue : d;
                        subscription.TotalDemand = demand;
                        // returns Long.MinValue if the subscription is to be terminated
                        var x = DispatchFromBufferAndReturnRemainingRequested(demand, subscription);
                        if (x == long.MinValue)
                        {
                            _endOfStream.Apply(subscription.Subscriber);
                            UnregisterSubscriptionInternal(subscription);
                        }
                        else
                        {
                            subscription.TotalDemand = x;
                            RequestFromUpstreamIfRequired();
                        }
                    }
                }
            }
        }

        private long DispatchFromBufferAndReturnRemainingRequested(long requested, ISubscriptionWithCursor<T> subscription)
        {
            while (requested > 0)
            {
                if (_buffer.Value.Count(subscription) > 0)
                {
                    var pass = false;
                    try
                    {
                        subscription.Dispatch(_buffer.Value.Read(subscription));
                        pass = true;
                    }
                    catch (Exception e)
                    {
                        if (e is ISpecViolation)
                        {
                            UnregisterSubscriptionInternal(subscription);
                            pass = false;
                        }
                        else throw;
                    }

                    if (!pass) return long.MinValue;
                    else requested--;
                }
                else return requested;
            }

            // if request == 0
            // if we are at end-of-stream and have nothing more to read we complete now rather than after the next `requestMore`
            return !(_endOfStream is NotReached) && _buffer.Value.Count(subscription) == 0 ? long.MinValue : 0;
        }

        /// <summary>
        /// This method must be called by the implementing class whenever a new value is available to be pushed downstream.
        /// </summary>
        protected void PushToDownstream(T value)
        {
            if (_endOfStream is NotReached)
            {
                _pendingFromUpstream--;
                if (!_buffer.Value.Write(value)) throw new IllegalStateException("Output buffer overflow");
                if (Dispatch(_subscriptions)) RequestFromUpstreamIfRequired();
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
            if (_endOfStream is NotReached)
            {
                _endOfStream = Completed.Instance;
                _subscriptions = CompleteDoneSubscriptions(_subscriptions);
                if (_subscriptions.Count == 0) Shutdown(true);
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
                    Completed.Instance.Apply(subscription.Subscriber);
                }
                else result.Add(subscription);
            }
            return result;
        }

        /// <summary>
        /// This method must be called by the implementing class to push an error downstream.
        /// </summary>
        protected void AbortDownstream(Exception cause)
        {
            _endOfStream = new ErrorCompleted(cause);
            foreach (var subscription in _subscriptions)
                _endOfStream.Apply(subscription.Subscriber);
            _subscriptions.Clear();
        }

        /// <summary>
        /// Register a new subscriber.
        /// </summary>
        protected void RegisterSubscriber(ISubscriber<T> subscriber)
        {
            if (_endOfStream is NotReached)
                if (_subscriptions.Any(s => s.Subscriber.Equals(subscriber)))
                    ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "SubscriberManagement");
                else AddSubscription(subscriber);
            else if (_endOfStream is Completed && !_buffer.Value.IsEmpty)
                AddSubscription(subscriber);
            else _endOfStream.Apply(subscriber);
        }

        /// <summary>
        /// Called from <see cref="ISubscription.Cancel"/>, i.e. from another thread,
        /// override to add synchronization with itself, <see cref="Subscribe"/> and <see cref="MoreRequested"/>
        /// </summary>
        protected void UnregisterSubscription(ISubscriptionWithCursor<T> subscription)
        {
            UnregisterSubscriptionInternal(subscription);
        }

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
                    if (!(_endOfStream is NotReached))
                    {
                        _endOfStream = ShutDown;
                        CancelUpstream();
                    }

                    Shutdown(false);
                }
                else RequestFromUpstreamIfRequired(); // we might have removed a "blocking" subscriber and can continue now
            }
            // else ignore, we need to be idempotent
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
                if (e is ISpecViolation) UnregisterSubscriptionInternal(newSubscription);
                else throw;
            }
        }

        private void RequestFromUpstreamIfRequired()
        {
            var maxRequested = _subscriptions.Select(x => x.TotalDemand).Max();
            var desired = Math.Min(int.MaxValue, Math.Min(maxRequested, _buffer.Value.CapacityLeft) - _pendingFromUpstream);
            if (desired > 0)
            {
                _pendingFromUpstream += desired;
                RequestFromUpstream(desired);
            }
        }
    }
}