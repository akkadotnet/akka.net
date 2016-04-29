//-----------------------------------------------------------------------
// <copyright file="ActorPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Reactive.Streams;
using System.Threading;
using Akka.Actor;
using Akka.Pattern;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;

namespace Akka.Streams.Actors
{
    #region Internal messages

    [Serializable]
    public sealed class Subscribe<T>
    {
        public readonly ISubscriber<T> Subscriber;
        public Subscribe(ISubscriber<T> subscriber)
        {
            Subscriber = subscriber;
        }
    }

    [Serializable]
    public enum LifecycleState
    {
        PreSubscriber,
        Active,
        Canceled,
        Completed,
        CompleteThenStop,
        ErrorEmitted
    }

    #endregion

    public interface IActorPublisherMessage { }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor when the stream
    /// subscriber requests more elements.
    /// </summary>
    [Serializable]
    public sealed class Request : IActorPublisherMessage
    {
        public readonly long Count;
        public Request(long count)
        {
            Count = count;
        }
    }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor when the stream
    /// subscriber cancels the subscription.
    /// </summary>
    [Serializable]
    public sealed class Cancel : IActorPublisherMessage
    {
        public static readonly Cancel Instance = new Cancel();
        private Cancel() { }
    }

    /// <summary>
    /// This message is delivered to the <see cref="ActorPublisher{T}"/> actor in order to signal
    /// the exceeding of an subscription timeout. Once the actor receives this message, this
    /// publisher will already be in cancelled state, thus the actor should clean-up and stop itself.
    /// </summary>
    [Serializable]
    public sealed class SubscriptionTimeoutExceeded : IActorPublisherMessage
    {
        public static readonly SubscriptionTimeoutExceeded Instance = new SubscriptionTimeoutExceeded();
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
    public abstract class ActorPublisher<T> : ActorBase
    {
        protected readonly ActorPublisherState State = ActorPublisherState.Instance.Apply(Context.System);
        private long _demand;
        private LifecycleState _lifecycleState = LifecycleState.PreSubscriber;
        private ISubscriber<T> _subscriber;
        private ICancelable _scheduledSubscriptionTimeout = NoopSubscriptionTimeout.Instance;

        // case and stop fields are used only when combined with LifecycleState.ErrorEmitted
        private OnErrorBlock _onError;

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
            => _lifecycleState == LifecycleState.Active || _lifecycleState == LifecycleState.PreSubscriber;

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
        /// Send an element to the stream subscriber. You are allowed to send as many elements
        /// as have been requested by the stream subscriber. This amount can be inquired with
        /// <see cref="TotalDemand"/>. It is only allowed to use <see cref="OnNext"/> when
        /// <see cref="IsActive"/> and <see cref="TotalDemand"/> &gt; 0,
        /// otherwise <see cref="OnNext"/> will throw <see cref="IllegalStateException"/>.
        /// </summary>
        public void OnNext(T element)
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                case LifecycleState.CompleteThenStop:
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
                case LifecycleState.ErrorEmitted: throw new IllegalStateException("OnNext must not be called after OnError");
                case LifecycleState.Completed: throw new IllegalStateException("OnNext must not be called after OnComplete");
                case LifecycleState.Canceled: break;
            }
        }

        /// <summary>
        /// Complete the stream. After that you are not allowed to
        /// call <see cref="OnNext"/>, <see cref="OnError"/> and <see cref="OnComplete"/>.
        /// </summary>
        public void OnComplete()
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                case LifecycleState.CompleteThenStop:
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
                case LifecycleState.Completed: throw new IllegalStateException("OnComplete must only be called once");
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
        public void OnError(Exception cause)
        {
            switch (_lifecycleState)
            {
                case LifecycleState.Active:
                case LifecycleState.PreSubscriber:
                case LifecycleState.CompleteThenStop:
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
                case LifecycleState.Completed: throw new IllegalStateException("OnError must not be called after OnComplete");
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

        protected override bool AroundReceive(Receive receive, object message)
        {
            if (message is Request)
            {
                var req = (Request) message;
                if (req.Count < 1)
                {
                    if (_lifecycleState == LifecycleState.Active)
                        OnError(new IllegalStateException("Number of requested elements must be positive"));
                    else
                        base.AroundReceive(receive, message);
                }
                else
                {
                    _demand += req.Count;
                    if (_demand < 0) _demand = long.MaxValue; // long overflow: effectively unbounded
                    base.AroundReceive(receive, message);
                }
            }
            else if (message is Subscribe<T>)
            {
                var sub = (Subscribe<T>) message;
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
                        ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                        ReactiveStreamsCompliance.TryOnError(subscriber, _subscriber == subscriber
                            ? new IllegalStateException("Cannot subscribe the same subscriber multiple times")
                            : new IllegalStateException("Only supports one subscriber"));
                        break;
                }
            }
            else if (message is Cancel)
            {
                CancelSelf();
                base.AroundReceive(receive, message);
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

        public override void AroundPreStart()
        {
            base.AroundPreStart();

            if (SubscriptionTimeout != Timeout.InfiniteTimeSpan)
            {
                _scheduledSubscriptionTimeout = new Cancelable(Context.System.Scheduler);
                Context.System.Scheduler.ScheduleTellOnce(SubscriptionTimeout, Self, SubscriptionTimeoutExceeded.Instance, Self, _scheduledSubscriptionTimeout);
            }
        }

        public override void AroundPreRestart(Exception cause, object message)
        {
            // some state must survive restart
            State.Set(Self, new ActorPublisherState.State(_subscriber, _demand, _lifecycleState));
            base.AroundPreRestart(cause, message);
        }

        public override void AroundPostRestart(Exception cause, object message)
        {
            var s = State.Remove(Self);
            if (s != null)
            {
                _subscriber = (ISubscriber<T>) s.Subscriber;
                _demand = s.Demand;
                _lifecycleState = s.LifecycleState;
            }

            base.AroundPostRestart(cause, message);
        }

        public override void AroundPostStop()
        {
            State.Remove(Self);
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

    public static class ActorPublisher
    {
        public static IPublisher<T> Create<T>(IActorRef @ref) => new ActorPublisherImpl<T>(@ref);
    }

    public sealed class ActorPublisherImpl<T> : IPublisher<T>
    {
        private readonly IActorRef _ref;

        public ActorPublisherImpl(IActorRef @ref)
        {
            if(@ref == null) throw new ArgumentNullException(nameof(@ref), "ActorPublisherImpl requires IActorRef to be defined");
            _ref = @ref;
        }

        public void Subscribe(ISubscriber<T> subscriber)
        {
            if (subscriber == null) throw new ArgumentNullException(nameof(subscriber), "Subscriber must not be null");
            _ref.Tell(new Subscribe<T>(subscriber));
        }

        void IPublisher.Subscribe(ISubscriber subscriber) => Subscribe((ISubscriber<T>) subscriber);
    }

    public sealed class ActorPublisherSubscription : ISubscription
    {
        private readonly IActorRef _ref;

        public ActorPublisherSubscription(IActorRef @ref)
        {
            if (@ref == null) throw new ArgumentNullException(nameof(@ref), "ActorPublisherSubscription requires IActorRef to be defined");
            _ref = @ref;
        }

        public void Request(long n) => _ref.Tell(new Request(n));

        public void Cancel() => _ref.Tell(Actors.Cancel.Instance);
    }

    public sealed class OnErrorBlock
    {
        public readonly Exception Cause;
        public readonly bool Stop;

        public OnErrorBlock(Exception cause, bool stop)
        {
            Cause = cause;
            Stop = stop;
        }
    }

    public class ActorPublisherState : ExtensionIdProvider<ActorPublisherState>, IExtension
    {
        public sealed class State
        {
            public readonly ISubscriber Subscriber;
            public readonly long Demand;
            public readonly LifecycleState LifecycleState;

            public State(ISubscriber subscriber, long demand, LifecycleState lifecycleState)
            {
                Subscriber = subscriber;
                Demand = demand;
                LifecycleState = lifecycleState;
            }
        }

        private readonly ConcurrentDictionary<IActorRef, State> _state = new ConcurrentDictionary<IActorRef, State>();

        public static readonly ActorPublisherState Instance = new ActorPublisherState();

        private ActorPublisherState() { }

        public State Get(IActorRef actorRef)
        {
            State state;
            return _state.TryGetValue(actorRef, out state) ? state : null;
        }

        public void Set(IActorRef actorRef, State s) => _state.AddOrUpdate(actorRef, s, (@ref, oldState) => s);

        public State Remove(IActorRef actorRef)
        {
            State s;
            return _state.TryRemove(actorRef, out s) ? s : null;
        }

        public override ActorPublisherState CreateExtension(ExtendedActorSystem system) => new ActorPublisherState();
    }
}