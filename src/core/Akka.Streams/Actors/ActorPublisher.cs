//-----------------------------------------------------------------------
// <copyright file="ActorPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using Akka.Actor;
using Akka.Event;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Reactive.Streams;

namespace Akka.Streams.Actors
{
    #region Internal messages

    /// <summary>
    /// Internal message sent to an actor-backed publisher to attach a stream subscriber.
    /// </summary>
    /// <typeparam name="T">The type of elements delivered to the subscriber.</typeparam>
    public sealed class Subscribe<T> : INoSerializationVerificationNeeded, IDeadLetterSuppression
    {
        /// <summary>
        /// The subscriber to attach.
        /// </summary>
        public readonly ISubscriber<T> Subscriber;
        /// <summary>
        /// Creates a subscription request message for the specified subscriber.
        /// </summary>
        /// <param name="subscriber">The stream subscriber to attach.</param>
        public Subscribe(ISubscriber<T> subscriber)
        {
            Subscriber = subscriber;
        }
    }

    /// <summary>
    /// Represents the lifecycle states tracked by <see cref="ActorPublisher{T}"/>.
    /// </summary>
    [Serializable]
    public enum LifecycleState
    {
        /// <summary>
        /// The publisher is awaiting its first subscriber.
        /// </summary>
        PreSubscriber,
        /// <summary>
        /// A subscriber is attached and the publisher can deliver requested elements.
        /// </summary>
        Active,
        /// <summary>
        /// The subscriber canceled the subscription.
        /// </summary>
        Canceled,
        /// <summary>
        /// The publisher signaled successful completion.
        /// </summary>
        Completed,
        /// <summary>
        /// Completion was requested and the actor should stop after signaling the subscriber.
        /// </summary>
        CompleteThenStop,
        /// <summary>
        /// The publisher signaled a terminal error.
        /// </summary>
        ErrorEmitted
    }

    #endregion

    /// <summary>
    /// Marker for messages used by the actor-backed publisher protocol.
    /// </summary>
    public interface IActorPublisherMessage: IDeadLetterSuppression { }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor when the stream
    /// subscriber requests more elements.
    /// </summary>
    [Serializable]
    public sealed class Request : IActorPublisherMessage, INoSerializationVerificationNeeded
    {
        /// <summary>
        /// The number of elements requested by the subscriber.
        /// </summary>
        public readonly long Count;

        /// <summary>
        /// Creates a request message for the specified demand count.
        /// </summary>
        /// <param name="count">The requested number of elements.</param>
        public Request(long count)
        {
            Count = count;
        }

        /// <summary>
        /// INTERNAL API: needed for stash support
        /// </summary>
        internal void MarkProcessed() => IsProcessed = true;

        /// <summary>
        /// INTERNAL API: needed for stash support
        /// </summary>
        internal bool IsProcessed { get; private set; }
    }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor when the stream
    /// subscriber cancels the subscription.
    /// </summary>
    [Serializable]
    public sealed class Cancel : IActorPublisherMessage, INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Singleton message indicating that the subscriber canceled its subscription.
        /// </summary>
        public static readonly Cancel Instance = new();
        private Cancel() { }
    }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor in order to signal
    /// the exceeding of an subscription timeout. Once the actor receives this message, this
    /// publisher will already be in cancelled state, thus the actor should clean-up and stop itself.
    /// </summary>
    [Serializable]
    public sealed class SubscriptionTimeoutExceeded : IActorPublisherMessage, INoSerializationVerificationNeeded
    {
        /// <summary>
        /// Singleton message indicating that the publisher's subscription timeout elapsed.
        /// </summary>
        public static readonly SubscriptionTimeoutExceeded Instance = new();
        private SubscriptionTimeoutExceeded() { }
    }

    /// <summary>
    /// <para>
    /// Extend this actor to make it a stream publisher that keeps track of the subscription life cycle and
    /// requested elements.
    /// </para>
    /// <para>
    /// Create a <see cref="IPublisher{T}"/> backed by this actor with <see cref="ActorPublisher.Create{T}"/>.
    /// </para>
    /// <para>
    /// It can be attached to a <see cref="ISubscriber{T}"/> or be used as an input source for a
    /// <see cref="IFlow{T,TMat}"/>. You can only attach one subscriber to this publisher.
    /// </para>
    /// <para>
    /// The life cycle state of the subscription is tracked with the following boolean members:
    /// <see cref="IsActive"/>, <see cref="IsCompleted"/>, <see cref="IsErrorEmitted"/>,
    /// and <see cref="IsCanceled"/>.
    /// </para>
    /// <para>
    /// You send elements to the stream by calling <see cref="OnNext"/>. You are allowed to send as many
    /// elements as have been requested by the stream subscriber. This amount can be inquired with
    /// <see cref="TotalDemand"/>. It is only allowed to use <see cref="OnNext"/> when <see cref="IsActive"/>
    /// <see cref="TotalDemand"/> &gt; 0, otherwise <see cref="OnNext"/> will throw
    /// <see cref="IllegalStateException"/>.
    /// </para>
    /// <para>
    /// When the stream subscriber requests more elements the <see cref="Request"/> message
    /// is delivered to this actor, and you can act on that event. The <see cref="TotalDemand"/>
    /// is updated automatically.
    /// </para>
    /// <para>
    /// When the stream subscriber cancels the subscription the <see cref="Cancel"/> message
    /// is delivered to this actor. After that subsequent calls to <see cref="OnNext"/> will be ignored.
    /// </para>
    /// <para>
    /// You can complete the stream by calling <see cref="OnComplete"/>. After that you are not allowed to
    /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
    /// </para>
    /// <para>
    /// You can terminate the stream with failure by calling <see cref="OnError"/>. After that you are not allowed to
    /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
    /// </para>
    /// <para>
    /// If you suspect that this <see cref="ActorPublisher{T}"/> may never get subscribed to,
    /// you can override the <see cref="SubscriptionTimeout"/> method to provide a timeout after which
    /// this Publisher should be considered canceled. The actor will be notified when
    /// the timeout triggers via an <see cref="SubscriptionTimeoutExceeded"/> message and MUST then
    /// perform cleanup and stop itself.
    /// </para>
    /// <para>
    /// If the actor is stopped the stream will be completed, unless it was not already terminated with
    /// failure, completed or canceled.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of elements published by this actor.</typeparam>
    public abstract class ActorPublisher<T> : ActorBase
    {
        private readonly ActorPublisherState _state = ActorPublisherState.Instance.Apply(Context.System);
        private long _demand;
        private LifecycleState _lifecycleState = LifecycleState.PreSubscriber;
        private ISubscriber<T> _subscriber;
        private ICancelable _scheduledSubscriptionTimeout = NoopSubscriptionTimeout.Instance;

        // case and stop fields are used only when combined with LifecycleState.ErrorEmitted
        private OnErrorBlock _onError;

        /// <summary>
        /// Initializes a publisher with no subscription timeout.
        /// </summary>
        protected ActorPublisher()
        {
            SubscriptionTimeout = Timeout.InfiniteTimeSpan;
        }

        /// <summary>
        /// Subscription timeout after which this actor will become Canceled and reject any incoming "late" subscriber.
        ///
        /// The actor will receive an <see cref="SubscriptionTimeoutExceeded"/> message upon which it
        /// MUST react by performing all necessary cleanup and stopping itself.
        ///
        /// Use this feature in order to avoid leaking actors when you suspect that this Publisher may never get subscribed to by some Subscriber.
        /// </summary>
        /// <summary>
        /// <para>
        /// Subscription timeout after which this actor will become Canceled and reject any incoming "late" subscriber.
        /// </para>
        /// <para>
        /// The actor will receive an <see cref="SubscriptionTimeoutExceeded"/> message upon which it
        /// MUST react by performing all necessary cleanup and stopping itself.
        /// </para>
        /// <para>
        /// Use this feature in order to avoid leaking actors when you suspect that this Publisher
        /// may never get subscribed to by some Subscriber.
        /// </para>
        /// </summary>
        public TimeSpan SubscriptionTimeout { get; protected set; }

        /// <summary>
        /// The state when the publisher is active, i.e. before the subscriber is attached
        /// and when an subscriber is attached. It is allowed to
        /// call <see cref="OnComplete"/> and <see cref="OnError"/> in this state. It is
        /// allowed to call <see cref="OnNext"/> in this state when <see cref="TotalDemand"/>
        /// is greater than zero.
        /// </summary>
        public bool IsActive
            => _lifecycleState is LifecycleState.Active or LifecycleState.PreSubscriber;

        /// <summary>
        /// Total number of requested elements from the stream subscriber.
        /// This actor automatically keeps tracks of this amount based on
        /// incoming request messages and outgoing <see cref="OnNext"/>.
        /// </summary>
        public long TotalDemand => _demand;

        /// <summary>
        /// The terminal state after calling <see cref="OnComplete"/>. It is not allowed to
        /// <see cref="OnNext"/>, <see cref="OnError"/>, and <see cref="OnComplete"/> in this state.
        /// </summary>
        public bool IsCompleted => _lifecycleState == LifecycleState.Completed;

        /// <summary>
        /// The terminal state after calling <see cref="OnError"/>. It is not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/>, and <see cref="OnComplete"/> in this state.
        /// </summary>
        public bool IsErrorEmitted => _lifecycleState == LifecycleState.ErrorEmitted;

        /// <summary>
        /// The state after the stream subscriber has canceled the subscription.
        /// It is allowed to call <see cref="OnNext"/>, <see cref="OnError"/>, and <see cref="OnComplete"/> in
        /// this state, but the calls will not perform anything.
        /// </summary>
        public bool IsCanceled => _lifecycleState == LifecycleState.Canceled;


        /// <summary>
        /// Sends an element to the stream subscriber. You are allowed to send as many elements
        /// as have been requested by the stream subscriber. This amount can be inquired with
        /// <see cref="TotalDemand"/>. It is only allowed to use <see cref="OnNext"/> when
        /// <see cref="IsActive"/> and <see cref="TotalDemand"/> &gt; 0,
        /// otherwise <see cref="OnNext"/> will throw <see cref="IllegalStateException"/>.
        /// </summary>
        /// <param name="element">The element to deliver to the subscriber.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown for a number of reasons. These include:
        /// <dl>
        ///   <dt>when in the <see cref="LifecycleState.Active"/> or <see cref="LifecycleState.PreSubscriber"/> state</dt>
        ///   <dd>This exception is thrown when the <see cref="ActorPublisher{T}"/> has zero <see cref="TotalDemand"/>.</dd>
        ///   <dt>when in the <see cref="LifecycleState.ErrorEmitted"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already terminated due to an error.</dd>
        ///   <dt>when in the <see cref="LifecycleState.Completed"/> or <see cref="LifecycleState.CompleteThenStop"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already completed.</dd>
        /// </dl>
        /// </exception>
        public void OnNext(T element)
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                    if (_demand > 0)
                    {
                        _demand--;
                        ReactiveStreamsCompliance.TryOnNext(_subscriber, element);
                    }
                    else
                    {
                        throw new IllegalStateException(
                            "OnNext is not allowed when the stream has not requested elements, total demand was 0");
                    }
                    break;
                case LifecycleState.ErrorEmitted:
                    throw new IllegalStateException("OnNext must not be called after OnError");
                case LifecycleState.Completed:
                case LifecycleState.CompleteThenStop:
                    throw new IllegalStateException("OnNext must not be called after OnComplete");
                case LifecycleState.Canceled: break;
            }
        }

        /// <summary>
        /// Complete the stream. After that you are not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
        /// </summary>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown for a number of reasons. These include:
        /// <dl>
        ///   <dt>when in the <see cref="LifecycleState.ErrorEmitted"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already terminated due to an error.</dd>
        ///   <dt>when in the <see cref="LifecycleState.Completed"/> or <see cref="LifecycleState.CompleteThenStop"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already completed.</dd>
        /// </dl>
        /// </exception>
        public void OnComplete()
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                    _lifecycleState = LifecycleState.Completed;
                    _onError = null;
                    if (_subscriber != null)
                    {
                        // otherwise onComplete will be called when the subscription arrives
                        try
                        {
                            ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                        }
                        finally
                        {
                            _subscriber = null;
                        }
                    }
                    break;
                case LifecycleState.ErrorEmitted: throw new IllegalStateException("OnComplete must not be called after OnError");
                case LifecycleState.Completed:
                case LifecycleState.CompleteThenStop: throw new IllegalStateException("OnComplete must only be called once");
                case LifecycleState.Canceled: break;
            }
        }

        /// <summary>
        /// <para>
        /// Complete the stream. After that you are not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
        /// </para>
        /// <para>
        /// After signalling completion the Actor will then stop itself as it has completed the protocol.
        /// When <see cref="OnComplete"/> is called before any <see cref="ISubscriber{T}"/> has had the chance to subscribe
        /// to this <see cref="ActorPublisher{T}"/> the completion signal (and therefore stopping of the Actor as well)
        /// will be delayed until such <see cref="ISubscriber{T}"/> arrives.
        /// </para>
        /// </summary>
        public void OnCompleteThenStop()
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                    _lifecycleState = LifecycleState.CompleteThenStop;
                    _onError = null;
                    if (_subscriber != null)
                    {
                        // otherwise onComplete will be called when the subscription arrives
                        try
                        {
                            ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                        }
                        finally
                        {
                            Context.Stop(Self);
                        }
                    }
                    break;
                default: OnComplete(); break;
            }
        }

        /// <summary>
        /// Terminate the stream with failure. After that you are not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
        /// </summary>
        /// <param name="cause">The failure signaled to the subscriber.</param>
        /// <exception cref="IllegalStateException">
        /// This exception is thrown for a number of reasons. These include:
        /// <dl>
        ///   <dt>when in the <see cref="LifecycleState.ErrorEmitted"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already terminated due to an error.</dd>
        ///   <dt>when in the <see cref="LifecycleState.Completed"/> or <see cref="LifecycleState.CompleteThenStop"/> state</dt>
        ///   <dd>This exception is thrown when this <see cref="ActorPublisher{T}"/> has already completed.</dd>
        /// </dl>
        /// </exception>
        public void OnError(Exception cause)
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                    _lifecycleState = LifecycleState.ErrorEmitted;
                    _onError = new OnErrorBlock(cause, false);
                    if (_subscriber != null)
                    {
                        // otherwise onError will be called when the subscription arrives
                        try
                        {
                            ReactiveStreamsCompliance.TryOnError(_subscriber, cause);
                        }
                        finally
                        {
                            _subscriber = null;
                        }
                    }
                    break;
                case LifecycleState.ErrorEmitted: throw new IllegalStateException("OnError must only be called once");
                case LifecycleState.Completed:
                case LifecycleState.CompleteThenStop: throw new IllegalStateException("OnError must not be called after OnComplete");
                case LifecycleState.Canceled: break;
            }
        }

        /// <summary>
        /// <para>
        /// Terminate the stream with failure. After that you are not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
        /// </para>
        /// <para>
        /// After signalling the Error the Actor will then stop itself as it has completed the protocol.
        /// When <see cref="OnError"/> is called before any <see cref="ISubscriber{T}"/> has had the chance to subscribe
        /// to this <see cref="ActorPublisher{T}"/> the error signal (and therefore stopping of the Actor as well)
        /// will be delayed until such <see cref="ISubscriber{T}"/> arrives.
        /// </para>
        /// </summary>
        /// <param name="cause">The failure signaled to the subscriber before the actor stops.</param>
        public void OnErrorThenStop(Exception cause)
        {
            switch (_lifecycleState)
            {

                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                    _lifecycleState = LifecycleState.ErrorEmitted;
                    _onError = new OnErrorBlock(cause, stop: true);
                    if (_subscriber != null)
                    {
                        // otherwise onError will be called when the subscription arrives
                        try
                        {
                            ReactiveStreamsCompliance.TryOnError(_subscriber, cause);
                        }
                        finally
                        {
                            Context.Stop(Self);
                        }
                    }
                    break;
                default: OnError(cause); break;
            }
        }

        #region Internal API

        /// <summary>
        /// Intercepts publisher protocol messages and delegates only messages that also need the actor's receive handler.
        /// </summary>
        /// <param name="receive">The actor's receive handler.</param>
        /// <param name="message">The message being processed.</param>
        /// <returns><see langword="true"/> for publisher protocol messages; otherwise, the result from the base actor implementation.</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
        {
            if (message is Request req)
            {
                if (req.IsProcessed)
                {
                    // it's an unstashed Request, demand is already handled
                    base.AroundReceive(receive, req);
                }
                else
                {
                    if (req.Count < 1)
                    {
                        if (_lifecycleState == LifecycleState.Active)
                            OnError(new ArgumentException("Number of requested elements must be positive. Rule 3.9"));
                    }
                    else
                    {
                        _demand += req.Count;
                        if (_demand < 0)
                            _demand = long.MaxValue; // long overflow: effectively unbounded
                        req.MarkProcessed();
                        base.AroundReceive(receive, req);
                    }
                }
            }
            else if (message is Subscribe<T> sub)
            {
                var subscriber = sub.Subscriber;
                switch (_lifecycleState)
                {
                    case LifecycleState.PreSubscriber:
                        _scheduledSubscriptionTimeout.Cancel();
                        _subscriber = subscriber;
                        _lifecycleState = LifecycleState.Active;
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, new ActorPublisherSubscription(Self));
                        break;
                    case LifecycleState.ErrorEmitted:
                        if (_onError.Stop) Context.Stop(Self);
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                        ReactiveStreamsCompliance.TryOnError(subscriber, _onError.Cause);
                        break;
                    case LifecycleState.Completed:
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                        ReactiveStreamsCompliance.TryOnComplete(subscriber);
                        break;
                    case LifecycleState.CompleteThenStop:
                        Context.Stop(Self);
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                        ReactiveStreamsCompliance.TryOnComplete(subscriber);
                        break;
                    case LifecycleState.Active:
                    case LifecycleState.Canceled:
                        if(_subscriber == subscriber)
                            ReactiveStreamsCompliance.RejectDuplicateSubscriber(subscriber);
                        else
                            ReactiveStreamsCompliance.RejectAdditionalSubscriber(subscriber, "ActorPublisher");
                        break;
                }
            }
            else if (message is Cancel)
            {
                if (_lifecycleState != LifecycleState.Canceled)
                {
                    // possible to receive again in case of stash
                    CancelSelf();
                    base.AroundReceive(receive, message);
                }
            }
            else if (message is SubscriptionTimeoutExceeded)
            {
                if (!_scheduledSubscriptionTimeout.IsCancellationRequested)
                {
                    CancelSelf();
                    base.AroundReceive(receive, message);
                }
            }
            else return base.AroundReceive(receive, message);
            return true;
        }

        private void CancelSelf()
        {
            _lifecycleState = LifecycleState.Canceled;
            _subscriber = null;
            _onError = null;
            _demand = 0L;
        }

        /// <summary>
        /// Calls the base pre-start hook and schedules a subscription-timeout message when a finite timeout is configured.
        /// </summary>
        public override void AroundPreStart()
        {
            base.AroundPreStart();

            if (SubscriptionTimeout != Timeout.InfiniteTimeSpan)
            {
                _scheduledSubscriptionTimeout = new Cancelable(Context.System.Scheduler);
                // AK1004: Hard to replace this with IWithTimers implementation because SubscriptionTimeoutExceeded is
                // being handled inside AroundReceive which interferes with IWithTimers internal message handling.
                //
                // Naive IWithTimers implementation will break this actor behavior.
#pragma warning disable AK1004 
                Context.System.Scheduler.ScheduleTellOnce(SubscriptionTimeout, Self, SubscriptionTimeoutExceeded.Instance, Self, _scheduledSubscriptionTimeout);
#pragma warning restore AK1004
            }
        }

        /// <summary>
        /// Saves the subscriber, outstanding demand, and lifecycle state so they can be restored after restart.
        /// </summary>
        /// <param name="cause">The exception that caused the restart.</param>
        /// <param name="message">The message being processed when the restart was requested.</param>
        public override void AroundPreRestart(Exception cause, object message)
        {
            // some state must survive restart
            _state.Set(Self, new ActorPublisherState.State(UntypedSubscriber.FromTyped(_subscriber), _demand, _lifecycleState));
            base.AroundPreRestart(cause, message);
        }

        /// <summary>
        /// Restores publisher state saved before restart, then invokes the base post-restart hook.
        /// </summary>
        /// <param name="cause">The exception that caused the restart.</param>
        /// <param name="message">The message being processed when the restart was requested.</param>
        public override void AroundPostRestart(Exception cause, object message)
        {
            var s = _state.Remove(Self);
            if (s != null)
            {
                _subscriber = UntypedSubscriber.ToTyped<T>(s.Subscriber);
                _demand = s.Demand;
                _lifecycleState = s.LifecycleState;
            }

            base.AroundPostRestart(cause, message);
        }

        /// <summary>
        /// Cancels the subscription timeout, removes saved restart state, and completes an active subscriber before stopping.
        /// </summary>
        public override void AroundPostStop()
        {
            _scheduledSubscriptionTimeout.Cancel();
            _state.Remove(Self);
            try
            {
                if (_lifecycleState == LifecycleState.Active)
                    ReactiveStreamsCompliance.TryOnComplete(_subscriber);
            }
            finally
            {
                base.AroundPostStop();
            }
        }

        #endregion
    }

    /// <summary>
    /// Creates <see cref="IPublisher{T}"/> instances backed by actor references.
    /// </summary>
    public static class ActorPublisher
    {
        /// <summary>
        /// Creates a publisher that forwards subscriber and demand messages to an actor reference.
        /// </summary>
        /// <typeparam name="T">The type of elements published by the actor.</typeparam>
        /// <param name="ref">The actor that implements the <see cref="ActorPublisher{T}"/> protocol.</param>
        /// <returns>A publisher that sends subscription messages to <paramref name="ref"/>.</returns>
        public static IPublisher<T> Create<T>(IActorRef @ref) => new ActorPublisherImpl<T>(@ref);
    }

    /// <summary>
    /// An <see cref="IPublisher{T}"/> adapter that sends subscriber attachment messages to an actor.
    /// </summary>
    /// <typeparam name="T">The type of elements published by the actor.</typeparam>
    public sealed class ActorPublisherImpl<T> : IPublisher<T>
    {
        private readonly IActorRef _ref;

        /// <summary>
        /// Creates an actor-backed publisher adapter.
        /// </summary>
        /// <param name="ref">The actor that receives subscriber attachment messages.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="ref"/> is undefined.
        /// </exception>
        public ActorPublisherImpl(IActorRef @ref) =>
            _ref = @ref ?? throw new ArgumentNullException(nameof(@ref), "ActorPublisherImpl requires IActorRef to be defined");

        /// <summary>
        /// Attaches a subscriber by sending it to the backing actor.
        /// </summary>
        /// <param name="subscriber">The subscriber to attach.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="subscriber"/> is undefined.
        /// </exception>
        public void Subscribe(ISubscriber<T> subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber), "Subscriber must not be null");
            _ref.Tell(new Subscribe<T>(subscriber));
        }
    }

    /// <summary>
    /// An <see cref="ISubscription"/> adapter that sends demand and cancellation messages to an actor.
    /// </summary>
    public sealed class ActorPublisherSubscription : ISubscription
    {
        private readonly IActorRef _ref;

        /// <summary>
        /// Creates a subscription that communicates with the specified actor.
        /// </summary>
        /// <param name="ref">The actor receiving request and cancel messages.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="ref"/> is undefined.
        /// </exception>
        public ActorPublisherSubscription(IActorRef @ref) =>
            _ref = @ref ?? throw new ArgumentNullException(nameof(@ref), "ActorPublisherSubscription requires IActorRef to be defined");

        /// <summary>
        /// Requests the specified number of elements from the actor-backed publisher.
        /// </summary>
        /// <param name="n">The requested number of elements.</param>
        public void Request(long n) => _ref.Tell(new Request(n));

        /// <summary>
        /// Cancels the subscription by sending a cancellation message to the actor.
        /// </summary>
        public void Cancel() => _ref.Tell(Actors.Cancel.Instance);
    }

    /// <summary>
    /// Stores the failure signaled by an actor publisher and whether the actor should stop after delivering it.
    /// </summary>
    public sealed class OnErrorBlock
    {
        /// <summary>
        /// The failure sent to the subscriber.
        /// </summary>
        public readonly Exception Cause;
        /// <summary>
        /// Whether the actor is stopped after the failure is delivered to a subscriber.
        /// </summary>
        public readonly bool Stop;

        /// <summary>
        /// Creates the state associated with a pending or delivered error signal.
        /// </summary>
        /// <param name="cause">The failure signaled to the subscriber.</param>
        /// <param name="stop">Whether the actor should stop after delivering the error.</param>
        public OnErrorBlock(Exception cause, bool stop)
        {
            Cause = cause;
            Stop = stop;
        }
    }

    /// <summary>
    /// Actor-system extension that keeps publisher subscription state across actor restarts.
    /// </summary>
    internal sealed class ActorPublisherState : ExtensionIdProvider<ActorPublisherState>, IExtension
    {
        /// <summary>
        /// Snapshot of the publisher state retained while its actor restarts.
        /// </summary>
        public sealed class State
        {
            /// <summary>
            /// The subscriber associated with the publisher, if one was attached.
            /// </summary>
            public readonly IUntypedSubscriber Subscriber;
            /// <summary>
            /// The demand outstanding when the actor restarted.
            /// </summary>
            public readonly long Demand;
            /// <summary>
            /// The lifecycle state when the actor restarted.
            /// </summary>
            public readonly LifecycleState LifecycleState;

            /// <summary>
            /// Creates a restart snapshot for an actor publisher.
            /// </summary>
            /// <param name="subscriber">The subscriber associated with the publisher.</param>
            /// <param name="demand">The outstanding element demand.</param>
            /// <param name="lifecycleState">The publisher lifecycle state.</param>
            public State(IUntypedSubscriber subscriber, long demand, LifecycleState lifecycleState)
            {
                Subscriber = subscriber;
                Demand = demand;
                LifecycleState = lifecycleState;
            }
        }

        private readonly ConcurrentDictionary<IActorRef, State> _state = new();

        /// <summary>
        /// The extension identifier used to obtain publisher restart state storage.
        /// </summary>
        public static readonly ActorPublisherState Instance = new();

        private ActorPublisherState() { }

        /// <summary>
        /// Gets the saved state for an actor reference, if present.
        /// </summary>
        /// <param name="actorRef">The actor whose state is requested.</param>
        /// <returns>The saved state, or <see langword="null"/> when no state is stored for the actor.</returns>
        public State Get(IActorRef actorRef)
        {
            _state.TryGetValue(actorRef, out var state);
            return state;
        }

        /// <summary>
        /// Adds or replaces the saved state for an actor reference.
        /// </summary>
        /// <param name="actorRef">The actor whose state is stored.</param>
        /// <param name="s">The state to store.</param>
        public void Set(IActorRef actorRef, State s) => _state.AddOrUpdate(actorRef, s, (_, _) => s);

        /// <summary>
        /// Removes and returns the saved state for an actor reference, if present.
        /// </summary>
        /// <param name="actorRef">The actor whose state is removed.</param>
        /// <returns>The removed state, or <see langword="null"/> when no state was stored for the actor.</returns>
        public State Remove(IActorRef actorRef)
        {
            return _state.TryRemove(actorRef, out var s) ? s : null;
        }

        /// <summary>
        /// Creates an extension instance for an actor system.
        /// </summary>
        /// <param name="system">The actor system receiving the extension.</param>
        /// <returns>A new publisher state extension.</returns>
        public override ActorPublisherState CreateExtension(ExtendedActorSystem system) => new();
    }
}
