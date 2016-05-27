//-----------------------------------------------------------------------
// <copyright file="ActorSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Actors
{
    [Serializable]
    public sealed class OnSubscribe : INoSerializationVerificationNeeded
    {
        public readonly ISubscription Subscription;

        public OnSubscribe(ISubscription subscription)
        {
            Subscription = subscription;
        }
    }

    public interface IActorSubscriberMessage : INoSerializationVerificationNeeded { }

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

    /// <summary>
    /// <para>
    /// Extend this actor to make it a
    /// stream subscriber with full control of stream back pressure. It will receive
    /// <see cref="OnNext"/>, <see cref="OnComplete"/> and <see cref="OnError"/>
    /// messages from the stream. It can also receive other, non-stream messages, in
    /// the same way as any actor.
    /// </para>
    /// <para>
    /// Attach the actor as a <see cref="ISubscriber{T}"/> to the stream with
    /// <see cref="Create{T}"/>
    /// </para>
    /// <para>
    /// Subclass must define the <see cref="RequestStrategy"/> to control stream back pressure.
    /// After each incoming message the <see cref="ActorSubscriber"/> will automatically invoke
    /// the <see cref="IRequestStrategy.RequestDemand"/> and propagate the returned demand to the stream.
    /// The provided <see cref="WatermarkRequestStrategy"/> is a good strategy if the actor
    /// performs work itself.
    /// The provided <see cref="MaxInFlightRequestStrategy"/> is useful if messages are
    /// queued internally or delegated to other actors.
    /// You can also implement a custom <see cref="IRequestStrategy"/> or call <see cref="Request"/> manually
    /// together with <see cref="ZeroRequestStrategy"/> or some other strategy. In that case
    /// you must also call <see cref="Request"/> when the actor is started or when it is ready, otherwise
    /// it will not receive any elements.
    /// </para>
    /// </summary>
    public abstract class ActorSubscriber : ActorBase
    {
        private readonly ActorSubscriberState _state = ActorSubscriberState.Instance.Apply(Context.System);
        private ISubscription _subscription;
        private long _requested;
        private bool _canceled;

        public abstract IRequestStrategy RequestStrategy { get; }

        public bool IsCanceled => _canceled;

        /// <summary>
        /// The number of stream elements that have already been requested from upstream
        /// but not yet received.
        /// </summary>
        protected int RemainingRequested => _requested > int.MaxValue ? int.MaxValue : (int)_requested;

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
                var onSubscribe = (OnSubscribe) message;
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
            if (!_canceled)
                _subscription?.Cancel();
            base.AroundPostStop();
        }

        #endregion

        /// <summary>
        /// Request a number of elements from upstream.
        /// </summary>
        protected void Request(long n)
        {
            if (n > 0 && !_canceled)
            {
                // if we don't have a subscription yet, it will be requested when it arrives
                _subscription?.Request(n);
                _requested += n;
            }
        }

        /// <summary>
        /// <para>
        /// Cancel upstream subscription.
        /// No more elements will be delivered after cancel.
        /// </para>
        /// <para>
        /// The <see cref="ActorSubscriber"/> will be stopped immediatly after signalling cancelation.
        /// In case the upstream subscription has not yet arrived the Actor will stay alive
        /// until a subscription arrives, cancel it and then stop itself.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// Attach a <see cref="ActorSubscriber"/> actor as a <see cref="ISubscriber{T}"/>
        /// to a <see cref="IPublisher{T}"/> or <see cref="IFlow{TOut,TMat}"/>
        /// </summary>
        public static ISubscriber<T> Create<T>(IActorRef @ref) => new ActorSubscriberImpl<T>(@ref);
    }

    public sealed class ActorSubscriberImpl<T> : ISubscriber<T>
    {
        private readonly IActorRef _impl;

        public ActorSubscriberImpl(IActorRef impl)
        {
            if (impl == null) throw new ArgumentNullException(nameof(impl), "ActorSubscriberImpl requires actor impl to be defined");
            _impl = impl;
        }

        public void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription), "OnSubscribe requires subscription to be defined");
            _impl.Tell(new OnSubscribe(subscription));
        }

        public void OnNext(T element) => OnNext((object)element);

        public void OnNext(object element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element), "OnNext requires provided element not to be null");
            _impl.Tell(new OnNext(element));
        }

        public void OnError(Exception cause)
        {
            if (cause == null) throw new ArgumentNullException(nameof(cause), "OnError has no cause defined");
            _impl.Tell(new OnError(cause));
        }

        public void OnComplete() => _impl.Tell(Actors.OnComplete.Instance);
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

        public void Set(IActorRef actorRef, State s) => _state.AddOrUpdate(actorRef, s, (@ref, oldState) => s);

        public State Remove(IActorRef actorRef)
        {
            State s;
            return _state.TryRemove(actorRef, out s) ? s : null;
        }

        public override ActorSubscriberState CreateExtension(ExtendedActorSystem system) => new ActorSubscriberState();
    }
}