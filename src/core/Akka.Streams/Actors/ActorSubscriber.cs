//-----------------------------------------------------------------------
// <copyright file="ActorSubscriber.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Actors
{
    /// <summary>
    /// Message sent to an actor subscriber when the stream establishes its subscription.
    /// </summary>
    [Serializable]
    public sealed class OnSubscribe : INoSerializationVerificationNeeded, IDeadLetterSuppression
    {
        /// <summary>
        /// The stream subscription used to request elements or cancel upstream.
        /// </summary>
        public readonly ISubscription Subscription;

        /// <summary>
        /// Creates a message containing the stream subscription.
        /// </summary>
        /// <param name="subscription">The subscription provided by the publisher.</param>
        public OnSubscribe(ISubscription subscription)
        {
            Subscription = subscription;
        }
    }

    /// <summary>
    /// Marker interface for messages delivered by the stream subscriber adapter.
    /// </summary>
    public interface IActorSubscriberMessage : INoSerializationVerificationNeeded, IDeadLetterSuppression { }

    /// <summary>
    /// Message carrying an element delivered by the stream to an actor subscriber.
    /// </summary>
    [Serializable]
    public sealed class OnNext : IActorSubscriberMessage
    {
        /// <summary>
        /// The element delivered by the stream.
        /// </summary>
        public readonly object Element;

        /// <summary>
        /// Creates a message carrying a stream element.
        /// </summary>
        /// <param name="element">The element delivered by the stream.</param>
        public OnNext(object element)
        {
            Element = element;
        }
    }

    /// <summary>
    /// Message carrying the terminal failure signaled by the stream.
    /// </summary>
    [Serializable]
    public sealed class OnError : IActorSubscriberMessage
    {
        /// <summary>
        /// The failure signaled by the publisher.
        /// </summary>
        public readonly Exception Cause;

        /// <summary>
        /// Creates a message carrying the stream failure.
        /// </summary>
        /// <param name="cause">The failure signaled by the publisher.</param>
        public OnError(Exception cause)
        {
            Cause = cause;
        }
    }

    /// <summary>
    /// Singleton message indicating successful completion of the stream.
    /// </summary>
    [Serializable]
    public sealed class OnComplete : IActorSubscriberMessage
    {
        /// <summary>
        /// The singleton stream-completion message.
        /// </summary>
        public static readonly OnComplete Instance = new();
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

        /// <summary>
        /// The request strategy used to calculate demand after actor messages are processed.
        /// </summary>
        public abstract IRequestStrategy RequestStrategy { get; }

        /// <summary>
        /// Whether this actor subscriber has canceled or received a terminal stream signal.
        /// </summary>
        public bool IsCanceled => _canceled;

        /// <summary>
        /// The number of stream elements that have already been requested from upstream
        /// but not yet received.
        /// </summary>
        protected int RemainingRequested => _requested > int.MaxValue ? int.MaxValue : (int)_requested;

        /// <summary>
        /// Processes stream protocol messages and requests additional demand according to the request strategy.
        /// </summary>
        /// <param name="receive">The actor's receive handler.</param>
        /// <param name="message">The message being processed.</param>
        /// <returns>Always <see langword="true"/> after handling or delegating the message.</returns>
        protected internal override bool AroundReceive(Receive receive, object message)
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
            else if (message is OnSubscribe onSubscribe)
            {
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
            else if (message is OnComplete or OnError)
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

        /// <summary>
        /// Calls the base pre-start hook and requests the initial demand calculated by the request strategy.
        /// </summary>
        public override void AroundPreStart()
        {
            base.AroundPreStart();
            Request(RequestStrategy.RequestDemand(RemainingRequested));
        }

        /// <summary>
        /// Restores the subscription, outstanding demand, and cancellation state saved before restart, then recalculates demand.
        /// </summary>
        /// <param name="cause">The exception that caused the restart.</param>
        /// <param name="message">The message being processed when the restart was requested.</param>
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

        /// <summary>
        /// Saves the subscription, outstanding demand, and cancellation state so they can be restored after restart.
        /// </summary>
        /// <param name="cause">The exception that caused the restart.</param>
        /// <param name="message">The message being processed when the restart was requested.</param>
        public override void AroundPreRestart(Exception cause, object message)
        {
            // some state must survive restart
            _state.Set(Self, new ActorSubscriberState.State(_subscription, _requested, _canceled));
            base.AroundPreRestart(cause, message);
        }

        /// <summary>
        /// Removes saved restart state and cancels an active subscription before invoking the base post-stop hook.
        /// </summary>
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
        /// <param name="n">The number of elements to request. Non-positive values are ignored.</param>
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
        /// The <see cref="ActorSubscriber"/> will be stopped immediately after signaling cancellation.
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
        /// <typeparam name="T">The type of elements delivered to the actor subscriber.</typeparam>
        /// <param name="ref">The actor that handles the actor subscriber protocol.</param>
        /// <returns>A stream subscriber adapter that sends protocol messages to <paramref name="ref"/>.</returns>
        public static ISubscriber<T> Create<T>(IActorRef @ref) => new ActorSubscriberImpl<T>(@ref);
    }

    /// <summary>
    /// An <see cref="ISubscriber{T}"/> adapter that forwards stream signals to an actor.
    /// </summary>
    /// <typeparam name="T">The type of elements accepted by the subscriber.</typeparam>
    public sealed class ActorSubscriberImpl<T> : ISubscriber<T>
    {
        private readonly IActorRef _impl;

        /// <summary>
        /// Creates a subscriber adapter for the specified actor.
        /// </summary>
        /// <param name="impl">The actor that receives subscription, element, and terminal messages.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="impl"/> is undefined.
        /// </exception>
        public ActorSubscriberImpl(IActorRef impl) =>
            _impl = impl ?? throw new ArgumentNullException(nameof(impl), "ActorSubscriberImpl requires actor impl to be defined");

        /// <summary>
        /// Forwards the subscription to the actor as an <see cref="OnSubscribe"/> message.
        /// </summary>
        /// <param name="subscription">The subscription provided by the stream publisher.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="subscription"/> is undefined.
        /// </exception>
        public void OnSubscribe(ISubscription subscription)
        {
            if (subscription == null) throw new ArgumentNullException(nameof(subscription), "OnSubscribe requires subscription to be defined");
            _impl.Tell(new OnSubscribe(subscription));
        }

        /// <summary>
        /// Forwards an element to the actor as an <see cref="OnNext"/> message.
        /// </summary>
        /// <param name="element">The element delivered by the stream.</param>
        public void OnNext(T element) => OnNext((object)element);

        /// <summary>
        /// Forwards a non-null element to the actor as an <see cref="OnNext"/> message.
        /// </summary>
        /// <param name="element">The element delivered by the stream.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="element"/> is undefined.
        /// </exception>
        public void OnNext(object element)
        {
            if (element == null) throw new ArgumentNullException(nameof(element), "OnNext requires provided element not to be null");
            _impl.Tell(new OnNext(element));
        }

        /// <summary>
        /// Forwards the terminal failure to the actor as an <see cref="OnError"/> message.
        /// </summary>
        /// <param name="cause">The failure signaled by the stream publisher.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="cause"/> is undefined.
        /// </exception>
        public void OnError(Exception cause)
        {
            if (cause == null) throw new ArgumentNullException(nameof(cause), "OnError has no cause defined");
            _impl.Tell(new OnError(cause));
        }

        /// <summary>
        /// Forwards successful stream completion to the actor.
        /// </summary>
        public void OnComplete() => _impl.Tell(Actors.OnComplete.Instance);
    }

    /// <summary>
    /// Actor-system extension that keeps subscriber state across actor restarts.
    /// </summary>
    public sealed class ActorSubscriberState : ExtensionIdProvider<ActorSubscriberState>, IExtension
    {
        /// <summary>
        /// Snapshot of an actor subscriber's state retained while its actor restarts.
        /// </summary>
        [Serializable]
        public sealed class State
        {
            /// <summary>
            /// The stream subscription, if one has arrived.
            /// </summary>
            public readonly ISubscription Subscription;
            /// <summary>
            /// The element demand already requested from upstream and not yet received.
            /// </summary>
            public readonly long Requested;
            /// <summary>
            /// Whether the actor subscriber has canceled or received a terminal signal.
            /// </summary>
            public readonly bool IsCanceled;

            /// <summary>
            /// Creates a restart snapshot for an actor subscriber.
            /// </summary>
            /// <param name="subscription">The stream subscription, if available.</param>
            /// <param name="requested">The outstanding element demand.</param>
            /// <param name="isCanceled">Whether the subscriber has canceled or terminated.</param>
            public State(ISubscription subscription, long requested, bool isCanceled)
            {
                Subscription = subscription;
                Requested = requested;
                IsCanceled = isCanceled;
            }
        }

        /// <summary>
        /// The extension identifier used to obtain subscriber restart state storage.
        /// </summary>
        public static readonly ActorSubscriberState Instance = new();

        private ActorSubscriberState() { }

        private readonly ConcurrentDictionary<IActorRef, State> _state = new();

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
        /// <returns>A new subscriber state extension.</returns>
        public override ActorSubscriberState CreateExtension(ExtendedActorSystem system) => new();
    }
}
