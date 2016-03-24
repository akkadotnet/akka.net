using System;
using System.Collections.Concurrent;
using System.Reactive.Streams;
using System.Threading;
using Akka.Actor;
using Akka.Pattern;
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

    /**
     * This message is delivered to the [[ActorPublisher]] actor when the stream subscriber requests
     * more elements.
     * @param n number of requested elements
     */
    [Serializable]
    public sealed class Request : IActorPublisherMessage
    {
        public readonly long Count;
        public Request(long count)
        {
            Count = count;
        }
    }

    /**
     * This message is delivered to the [[ActorPublisher]] actor when the stream subscriber cancels the
     * subscription.
     */
    [Serializable]
    public sealed class Cancel : IActorPublisherMessage
    {
        public static readonly Cancel Instance = new Cancel();
        private Cancel() { }
    }

    /**
     * This message is delivered to the [[ActorPublisher]] actor in order to signal the exceeding of an subscription timeout.
     * Once the actor receives this message, this publisher will already be in cancelled state, thus the actor should clean-up and stop itself.
     */
    [Serializable]
    public sealed class SubscriptionTimeoutExceeded : IActorPublisherMessage
    {
        public static readonly SubscriptionTimeoutExceeded Instance = new SubscriptionTimeoutExceeded();
        private SubscriptionTimeoutExceeded() { }
    }

    /**
     * Extend/mixin this trait in your [[akka.actor.Actor]] to make it a
     * stream publisher that keeps track of the subscription life cycle and
     * requested elements.
     *
     * Create a [[org.reactivestreams.Publisher]] backed by this actor with Scala API [[ActorPublisher#apply]],
     * or Java API [[UntypedActorPublisher#create]] or Java API compatible with lambda expressions
     * [[AbstractActorPublisher#create]].
     *
     * It can be attached to a [[org.reactivestreams.Subscriber]] or be used as an input source for a
     * [[akka.stream.scaladsl.Flow]]. You can only attach one subscriber to this publisher.
     *
     * The life cycle state of the subscription is tracked with the following boolean members:
     * [[#isActive]], [[#isCompleted]], [[#isErrorEmitted]], and [[#isCanceled]].
     *
     * You send elements to the stream by calling [[#onNext]]. You are allowed to send as many
     * elements as have been requested by the stream subscriber. This amount can be inquired with
     * [[#totalDemand]]. It is only allowed to use `onNext` when `isActive` and `totalDemand > 0`,
     * otherwise `onNext` will throw `IllegalStateException`.
     *
     * When the stream subscriber requests more elements the [[ActorPublisher#Request]] message
     * is delivered to this actor, and you can act on that event. The [[#totalDemand]]
     * is updated automatically.
     *
     * When the stream subscriber cancels the subscription the [[ActorPublisher#Cancel]] message
     * is delivered to this actor. After that subsequent calls to `onNext` will be ignored.
     *
     * You can complete the stream by calling [[#onComplete]]. After that you are not allowed to
     * call [[#onNext]], [[#onError]] and [[#onComplete]].
     *
     * You can terminate the stream with failure by calling [[#onError]]. After that you are not allowed to
     * call [[#onNext]], [[#onError]] and [[#onComplete]].
     *
     * If you suspect that this [[ActorPublisher]] may never get subscribed to, you can override the [[#subscriptionTimeout]]
     * method to provide a timeout after which this Publisher should be considered canceled. The actor will be notified when
     * the timeout triggers via an [[akka.stream.actor.ActorPublisherMessage.SubscriptionTimeoutExceeded]] message and MUST then perform cleanup and stop itself.
     *
     * If the actor is stopped the stream will be completed, unless it was not already terminated with
     * failure, completed or canceled.
     */
    public abstract class ActorPublisher<T> : ActorBase
    {
        protected readonly ActorPublisherState State = Context.System.WithExtension<ActorPublisherState, ActorPublisherState>();
        private long _demand;
        private LifecycleState _lifecycleState = LifecycleState.PreSubscriber;
        private ISubscriber<T> _subscriber;
        private ICancelable _scheduledSubscriptionTimeout = NoopSubscriptionTimeout.Instance;

        // case and stop fields are used only when combined with LifecycleState.ErrorEmitted
        private OnErrorBlock _onError;

        /**
         * Subscription timeout after which this actor will become Canceled and reject any incoming "late" subscriber.
         *
         * The actor will receive an [[SubscriptionTimeoutExceeded]] message upon which it
         * MUST react by performing all necessary cleanup and stopping itself.
         *
         * Use this feature in order to avoid leaking actors when you suspect that this Publisher may never get subscribed to by some Subscriber.
         */
        public TimeSpan SubscriptionTimeout { get; protected set; }

        /**
         * The state when the publisher is active, i.e. before the subscriber is attached
         * and when an subscriber is attached. It is allowed to
         * call [[#onComplete]] and [[#onError]] in this state. It is
         * allowed to call [[#onNext]] in this state when [[#totalDemand]]
         * is greater than zero.
         */

        public bool IsActive
            => _lifecycleState == LifecycleState.Active || _lifecycleState == LifecycleState.PreSubscriber;

        /**
         * Total number of requested elements from the stream subscriber.
         * This actor automatically keeps tracks of this amount based on
         * incoming request messages and outgoing `onNext`.
         */
        public long TotalDemand => _demand;

        /**
         * The terminal state after calling [[#onComplete]]. It is not allowed to
         * call [[#onNext]], [[#onError]], and [[#onComplete]] in this state.
         */
        public bool IsCompleted => _lifecycleState == LifecycleState.Completed;

        /**
         * The terminal state after calling [[#onError]]. It is not allowed to
         * call [[#onNext]], [[#onError]], and [[#onComplete]] in this state.
         */
        public bool IsErrorEmitted => _lifecycleState == LifecycleState.ErrorEmitted;

        /**
         * The state after the stream subscriber has canceled the subscription.
         * It is allowed to call [[#onNext]], [[#onError]], and [[#onComplete]] in
         * this state, but the calls will not perform anything.
         */
        public bool IsCanceled => _lifecycleState == LifecycleState.Canceled;


        /**
         * Send an element to the stream subscriber. You are allowed to send as many elements
         * as have been requested by the stream subscriber. This amount can be inquired with
         * [[#totalDemand]]. It is only allowed to use `onNext` when `isActive` and `totalDemand > 0`,
         * otherwise `onNext` will throw `IllegalStateException`.
         */
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

        /**
         * Complete the stream. After that you are not allowed to
         * call [[#onNext]], [[#onError]] and [[#onComplete]].
         */
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

        /**
         * Complete the stream. After that you are not allowed to
         * call [[#onNext]], [[#onError]] and [[#onComplete]].
         *
         * After signalling completion the Actor will then stop itself as it has completed the protocol.
         * When [[#onComplete]] is called before any [[Subscriber]] has had the chance to subscribe
         * to this [[ActorPublisher]] the completion signal (and therefore stopping of the Actor as well)
         * will be delayed until such [[Subscriber]] arrives.
         */
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

        /**
         * Terminate the stream with failure. After that you are not allowed to
         * call [[#onNext]], [[#onError]] and [[#onComplete]].
         */
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

        /**
         * Terminate the stream with failure. After that you are not allowed to
         * call [[#onNext]], [[#onError]] and [[#onComplete]].
         *
         * After signalling the Error the Actor will then stop itself as it has completed the protocol.
         * When [[#onError]] is called before any [[Subscriber]] has had the chance to subscribe
         * to this [[ActorPublisher]] the error signal (and therefore stopping of the Actor as well)
         * will be delayed until such [[Subscriber]] arrives.
         */
        public void OnErrorThenStop(Exception cause)
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
                {
                    ReactiveStreamsCompliance.TryOnComplete(_subscriber);
                }
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
        public static IPublisher<T> Create<T>(IActorRef @ref)
        {
            return new ActorPublisherImpl<T>(@ref);
        }
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

        void IPublisher.Subscribe(ISubscriber subscriber)
        {
            Subscribe((ISubscriber<T>) subscriber);
        }
    }

    public sealed class ActorPublisherSubscription : ISubscription
    {
        private readonly IActorRef _ref;

        public ActorPublisherSubscription(IActorRef @ref)
        {
            if (@ref == null) throw new ArgumentNullException(nameof(@ref), "ActorPublisherSubscription requires IActorRef to be defined");
            _ref = @ref;
        }

        public void Request(long n)
        {
            _ref.Tell(new Request(n));
        }

        public void Cancel()
        {
            _ref.Tell(Actors.Cancel.Instance);
        }
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

        public State Get(IActorRef actorRef)
        {
            State state;
            return _state.TryGetValue(actorRef, out state) ? state : null;
        }

        public void Set(IActorRef actorRef, State s)
        {
            _state.AddOrUpdate(actorRef, s, (@ref, oldState) => s);
        }

        public State Remove(IActorRef actorRef)
        {
            State s;
            return _state.TryRemove(actorRef, out s) ? s : null;
        }

        public override ActorPublisherState CreateExtension(ExtendedActorSystem system)
        {
            return new ActorPublisherState();
        }
    }
}