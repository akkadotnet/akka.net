using System;
using System.Collections.Concurrent;
using System.Reactive.Streams;
using Akka.Actor;

namespace Akka.Streams.Actors
{
    [Serializable]
    public sealed class OnSubscribe
    {
        public readonly ISubscription Subscription;

        public OnSubscribe(ISubscription subscription)
        {
            Subscription = subscription;
        }
    }

    public interface IActorSubscriberMessage { }

    [Serializable]
    public sealed class OnNext : IActorSubscriberMessage
    {
        public readonly object Element;

        public OnNext(object element)
        {
            Element = element;
        }
    }

    [Serializable]
    public sealed class OnError : IActorSubscriberMessage
    {
        public readonly Exception Cause;

        public OnError(Exception cause)
        {
            Cause = cause;
        }
    }

    [Serializable]
    public sealed class OnComplete : IActorSubscriberMessage
    {
        public static readonly OnComplete Instance = new OnComplete();
        private OnComplete() { }
    }

    /**
     * Extend/mixin this trait in your [[akka.actor.Actor]] to make it a
     * stream subscriber with full control of stream back pressure. It will receive
     * [[ActorSubscriberMessage.OnNext]], [[ActorSubscriberMessage.OnComplete]] and [[ActorSubscriberMessage.OnError]]
     * messages from the stream. It can also receive other, non-stream messages, in
     * the same way as any actor.
     *
     * Attach the actor as a [[org.reactivestreams.Subscriber]] to the stream with
     * Scala API [[ActorSubscriber#apply]], or Java API [[UntypedActorSubscriber#create]] or
     * Java API compatible with lambda expressions [[AbstractActorSubscriber#create]].
     *
     * Subclass must define the [[RequestStrategy]] to control stream back pressure.
     * After each incoming message the `ActorSubscriber` will automatically invoke
     * the [[RequestStrategy#requestDemand]] and propagate the returned demand to the stream.
     * The provided [[WatermarkRequestStrategy]] is a good strategy if the actor
     * performs work itself.
     * The provided [[MaxInFlightRequestStrategy]] is useful if messages are
     * queued internally or delegated to other actors.
     * You can also implement a custom [[RequestStrategy]] or call [[#request]] manually
     * together with [[ZeroRequestStrategy]] or some other strategy. In that case
     * you must also call [[#request]] when the actor is started or when it is ready, otherwise
     * it will not receive any elements.
     */
    public abstract class ActorSubscriber : ActorBase
    {
        private readonly ActorSubscriberState _state = ActorSubscriberState.Instance.Apply(Context.System);
        private ISubscription _subscription = null;
        private long _requested = 0L;
        private bool _canceled = false;

        public abstract IRequestStrategy RequestStrategy { get; }

        public bool IsCanceled { get { return _canceled; } }

        /**
         * The number of stream elements that have already been requested from upstream
         * but not yet received.
         */
        protected int RemainingRequested { get { return _requested > int.MaxValue ? int.MaxValue : (int)_requested; } }

        protected override bool AroundReceive(Receive receive, object message)
        {
            if (message is OnNext)
            {
                _requested--;
                if (!_canceled)
                {
                    base.AroundReceive(receive, message);
                    Request(RequestStrategy.RequestDemand(RemainingRequested));
                }
            }
            else if (message is OnSubscribe)
            {
                var onSubscribe = message as OnSubscribe;
                if (_subscription == null)
                {
                    _subscription = onSubscribe.Subscription;
                    if (_canceled)
                    {
                        Context.Stop(Self);
                        onSubscribe.Subscription.Cancel();
                    }
                    else if (_requested != 0)
                    {
                        onSubscribe.Subscription.Request(RemainingRequested);
                    }
                }
                else
                {
                    onSubscribe.Subscription.Cancel();
                }
            }
            else if (message is OnComplete || message is OnError)
            {
                if (!_canceled)
                {
                    _canceled = true;
                    base.AroundReceive(receive, message);
                }
            }
            else
            {
                base.AroundReceive(receive, message);
                Request(RequestStrategy.RequestDemand(RemainingRequested));
            }
            return true;
        }

        #region Internal API

        public override void AroundPreStart()
        {
            base.AroundPreStart();
            Request(RequestStrategy.RequestDemand(RemainingRequested));
        }

        public override void AroundPostRestart(Exception cause, object message)
        {
            var s = _state.Remove(Self);
            // restore previous state
            if (s != null)
            {
                _subscription = s.Subscription;
                _requested = s.Requested;
                _canceled = s.IsCanceled;
            }

            base.AroundPostRestart(cause, message);
            Request(RequestStrategy.RequestDemand(RemainingRequested));
        }

        public override void AroundPreRestart(Exception cause, object message)
        {
            // some state must survive restart
            _state.Set(Self, new ActorSubscriberState.State(_subscription, _requested, _canceled));
            base.AroundPreRestart(cause, message);
        }

        public override void AroundPostStop()
        {
            _state.Remove(Self);
            if (!_canceled && _subscription != null)
                _subscription.Cancel();
            base.AroundPostStop();
        }

        #endregion

        /**
         * Request a number of elements from upstream.
         */
        protected void Request(long n)
        {
            if (n > 0 && !_canceled)
            {
                // if we don't have a subscription yet, it will be requested when it arrives
                if (_subscription != null) _subscription.Request(n);
                _requested += n;
            }
        }

        /**
         * Cancel upstream subscription.
         * No more elements will be delivered after cancel.
         *
         * The [[ActorSubscriber]] will be stopped immediatly after signalling cancelation.
         * In case the upstream subscription has not yet arrived the Actor will stay alive
         * until a subscription arrives, cancel it and then stop itself.
         */
        protected void Cancel()
        {
            if (!_canceled)
            {
                if (_subscription != null)
                {
                    Context.Stop(Self);
                    _subscription.Cancel();
                }
                else
                {
                    _canceled = true;
                }
            }
        }
    }

    public sealed class ActorSubscriberImpl<T> : ISubscriber<T>
    {
        private readonly IActorRef _impl;

        public ActorSubscriberImpl(IActorRef impl)
        {
            if (impl == null) throw new ArgumentNullException("impl", "ActorSubscriberImpl requires actor ref to be defined");
            _impl = impl;
        }

        public void OnNext(T element)
        {
            OnNext((object)element);
        }

        public void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException("subscription", "OnSubscribe requires subscription to be defined");
            _impl.Tell(new OnSubscribe(subscription));
        }

        public void OnNext(object element)
        {
            if (element == null) throw new ArgumentNullException("element", "OnNext requires provided element not to be null");
            _impl.Tell(new OnNext(element));
        }

        public void OnError(Exception cause)
        {
            if (cause == null) throw new ArgumentNullException("cause", "OnError has no cause defined");
            _impl.Tell(new OnError(cause));
        }

        public void OnComplete()
        {
            _impl.Tell(Actors.OnComplete.Instance);
        }
    }

    public sealed class ActorSubscriberState : ExtensionIdProvider<ActorSubscriberState>, IExtension
    {
        [Serializable]
        public sealed class State
        {
            public readonly ISubscription Subscription;
            public readonly long Requested;
            public readonly bool IsCanceled;

            public State(ISubscription subscription, long requested, bool isCanceled)
            {
                Subscription = subscription;
                Requested = requested;
                IsCanceled = isCanceled;
            }
        }

        public static readonly ActorSubscriberState Instance = new ActorSubscriberState();
        private ActorSubscriberState() { }

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

        public override ActorSubscriberState CreateExtension(ExtendedActorSystem system)
        {
            return new ActorSubscriberState();
        }
    }
}