﻿// -----------------------------------------------------------------------
//  <copyright file="ActorSubscriber.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Reactive.Streams;

namespace Akka.Streams.Actors;

/// <summary>
///     TBD
/// </summary>
[Serializable]
public sealed class OnSubscribe : INoSerializationVerificationNeeded, IDeadLetterSuppression
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly ISubscription Subscription;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="subscription">TBD</param>
    public OnSubscribe(ISubscription subscription)
    {
        Subscription = subscription;
    }
}

/// <summary>
///     TBD
/// </summary>
public interface IActorSubscriberMessage : INoSerializationVerificationNeeded, IDeadLetterSuppression
{
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
public sealed class OnNext : IActorSubscriberMessage
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly object Element;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="element">TBD</param>
    public OnNext(object element)
    {
        Element = element;
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
public sealed class OnError : IActorSubscriberMessage
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly Exception Cause;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="cause">TBD</param>
    public OnError(Exception cause)
    {
        Cause = cause;
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
public sealed class OnComplete : IActorSubscriberMessage
{
    /// <summary>
    ///     TBD
    /// </summary>
    public static readonly OnComplete Instance = new();

    private OnComplete()
    {
    }
}

/// <summary>
///     <para>
///         Extend this actor to make it a
///         stream subscriber with full control of stream back pressure. It will receive
///         <see cref="OnNext" />, <see cref="OnComplete" /> and <see cref="OnError" />
///         messages from the stream. It can also receive other, non-stream messages, in
///         the same way as any actor.
///     </para>
///     <para>
///         Attach the actor as a <see cref="ISubscriber{T}" /> to the stream with
///         <see cref="Create{T}" />
///     </para>
///     <para>
///         Subclass must define the <see cref="RequestStrategy" /> to control stream back pressure.
///         After each incoming message the <see cref="ActorSubscriber" /> will automatically invoke
///         the <see cref="IRequestStrategy.RequestDemand" /> and propagate the returned demand to the stream.
///         The provided <see cref="WatermarkRequestStrategy" /> is a good strategy if the actor
///         performs work itself.
///         The provided <see cref="MaxInFlightRequestStrategy" /> is useful if messages are
///         queued internally or delegated to other actors.
///         You can also implement a custom <see cref="IRequestStrategy" /> or call <see cref="Request" /> manually
///         together with <see cref="ZeroRequestStrategy" /> or some other strategy. In that case
///         you must also call <see cref="Request" /> when the actor is started or when it is ready, otherwise
///         it will not receive any elements.
///     </para>
/// </summary>
public abstract class ActorSubscriber : ActorBase
{
    private readonly ActorSubscriberState _state = ActorSubscriberState.Instance.Apply(Context.System);
    private long _requested;
    private ISubscription _subscription;

    /// <summary>
    ///     TBD
    /// </summary>
    public abstract IRequestStrategy RequestStrategy { get; }

    /// <summary>
    ///     TBD
    /// </summary>
    public bool IsCanceled { get; private set; }

    /// <summary>
    ///     The number of stream elements that have already been requested from upstream
    ///     but not yet received.
    /// </summary>
    protected int RemainingRequested => _requested > int.MaxValue ? int.MaxValue : (int)_requested;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="receive">TBD</param>
    /// <param name="message">TBD</param>
    /// <returns>TBD</returns>
    protected internal override bool AroundReceive(Receive receive, object message)
    {
        if (message is OnNext)
        {
            _requested--;
            if (!IsCanceled)
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
                if (IsCanceled)
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
            if (!IsCanceled)
            {
                IsCanceled = true;
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

    /// <summary>
    ///     Request a number of elements from upstream.
    /// </summary>
    /// <param name="n">TBD</param>
    protected void Request(long n)
    {
        if (n > 0 && !IsCanceled)
        {
            // if we don't have a subscription yet, it will be requested when it arrives
            _subscription?.Request(n);
            _requested += n;
        }
    }

    /// <summary>
    ///     <para>
    ///         Cancel upstream subscription.
    ///         No more elements will be delivered after cancel.
    ///     </para>
    ///     <para>
    ///         The <see cref="ActorSubscriber" /> will be stopped immediately after signaling cancellation.
    ///         In case the upstream subscription has not yet arrived the Actor will stay alive
    ///         until a subscription arrives, cancel it and then stop itself.
    ///     </para>
    /// </summary>
    protected void Cancel()
    {
        if (!IsCanceled)
        {
            if (_subscription != null)
            {
                Context.Stop(Self);
                _subscription.Cancel();
            }
            else
            {
                IsCanceled = true;
            }
        }
    }

    /// <summary>
    ///     Attach a <see cref="ActorSubscriber" /> actor as a <see cref="ISubscriber{T}" />
    ///     to a <see cref="IPublisher{T}" /> or <see cref="IFlow{TOut,TMat}" />
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <param name="ref">TBD</param>
    /// <returns>TBD</returns>
    public static ISubscriber<T> Create<T>(IActorRef @ref)
    {
        return new ActorSubscriberImpl<T>(@ref);
    }

    #region Internal API

    /// <summary>
    ///     TBD
    /// </summary>
    public override void AroundPreStart()
    {
        base.AroundPreStart();
        Request(RequestStrategy.RequestDemand(RemainingRequested));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="cause">TBD</param>
    /// <param name="message">TBD</param>
    public override void AroundPostRestart(Exception cause, object message)
    {
        var s = _state.Remove(Self);
        // restore previous state
        if (s != null)
        {
            _subscription = s.Subscription;
            _requested = s.Requested;
            IsCanceled = s.IsCanceled;
        }

        base.AroundPostRestart(cause, message);
        Request(RequestStrategy.RequestDemand(RemainingRequested));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="cause">TBD</param>
    /// <param name="message">TBD</param>
    public override void AroundPreRestart(Exception cause, object message)
    {
        // some state must survive restart
        _state.Set(Self, new ActorSubscriberState.State(_subscription, _requested, IsCanceled));
        base.AroundPreRestart(cause, message);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public override void AroundPostStop()
    {
        _state.Remove(Self);
        if (!IsCanceled)
            _subscription?.Cancel();
        base.AroundPostStop();
    }

    #endregion
}

/// <summary>
///     TBD
/// </summary>
/// <typeparam name="T">TBD</typeparam>
public sealed class ActorSubscriberImpl<T> : ISubscriber<T>
{
    private readonly IActorRef _impl;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="impl">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="impl" /> is undefined.
    /// </exception>
    public ActorSubscriberImpl(IActorRef impl)
    {
        if (impl == null)
            throw new ArgumentNullException(nameof(impl), "ActorSubscriberImpl requires actor impl to be defined");
        _impl = impl;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="subscription">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="subscription" /> is undefined.
    /// </exception>
    public void OnSubscribe(ISubscription subscription)
    {
        if (subscription == null)
            throw new ArgumentNullException(nameof(subscription), "OnSubscribe requires subscription to be defined");
        _impl.Tell(new OnSubscribe(subscription));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="element">TBD</param>
    public void OnNext(T element)
    {
        OnNext((object)element);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="cause">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="cause" /> is undefined.
    /// </exception>
    public void OnError(Exception cause)
    {
        if (cause == null) throw new ArgumentNullException(nameof(cause), "OnError has no cause defined");
        _impl.Tell(new OnError(cause));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public void OnComplete()
    {
        _impl.Tell(Actors.OnComplete.Instance);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="element">TBD</param>
    /// <exception cref="ArgumentNullException">
    ///     This exception is thrown when the specified <paramref name="element" /> is undefined.
    /// </exception>
    public void OnNext(object element)
    {
        if (element == null)
            throw new ArgumentNullException(nameof(element), "OnNext requires provided element not to be null");
        _impl.Tell(new OnNext(element));
    }
}

/// <summary>
///     TBD
/// </summary>
public sealed class ActorSubscriberState : ExtensionIdProvider<ActorSubscriberState>, IExtension
{
    /// <summary>
    ///     TBD
    /// </summary>
    public static readonly ActorSubscriberState Instance = new();

    private readonly ConcurrentDictionary<IActorRef, State> _state = new();

    private ActorSubscriberState()
    {
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="actorRef">TBD</param>
    /// <returns>TBD</returns>
    public State Get(IActorRef actorRef)
    {
        _state.TryGetValue(actorRef, out var state);
        return state;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="actorRef">TBD</param>
    /// <param name="s">TBD</param>
    /// <returns>TBD</returns>
    public void Set(IActorRef actorRef, State s)
    {
        _state.AddOrUpdate(actorRef, s, (_, _) => s);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="actorRef">TBD</param>
    /// <returns>TBD</returns>
    public State Remove(IActorRef actorRef)
    {
        return _state.TryRemove(actorRef, out var s) ? s : null;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="system">TBD</param>
    /// <returns>TBD</returns>
    public override ActorSubscriberState CreateExtension(ExtendedActorSystem system)
    {
        return new ActorSubscriberState();
    }

    /// <summary>
    ///     TBD
    /// </summary>
    [Serializable]
    public sealed class State
    {
        /// <summary>
        ///     TBD
        /// </summary>
        public readonly bool IsCanceled;

        /// <summary>
        ///     TBD
        /// </summary>
        public readonly long Requested;

        /// <summary>
        ///     TBD
        /// </summary>
        public readonly ISubscription Subscription;

        /// <summary>
        ///     TBD
        /// </summary>
        /// <param name="subscription">TBD</param>
        /// <param name="requested">TBD</param>
        /// <param name="isCanceled">TBD</param>
        public State(ISubscription subscription, long requested, bool isCanceled)
        {
            Subscription = subscription;
            Requested = requested;
            IsCanceled = isCanceled;
        }
    }
}