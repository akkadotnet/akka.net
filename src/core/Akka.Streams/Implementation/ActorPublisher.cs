﻿// -----------------------------------------------------------------------
//  <copyright file="ActorPublisher.cs" company="Akka.NET Project">
//      Copyright (C) 2009-2024 Lightbend Inc. <http://www.lightbend.com>
//      Copyright (C) 2013-2024 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Annotations;
using Akka.Event;
using Akka.Pattern;
using Akka.Util;
using Reactive.Streams;

namespace Akka.Streams.Implementation;

/// <summary>
///     TBD
/// </summary>
[Serializable]
internal sealed class SubscribePending
{
    /// <summary>
    ///     TBD
    /// </summary>
    public static readonly SubscribePending Instance = new();

    private SubscribePending()
    {
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
internal sealed class RequestMore : IDeadLetterSuppression
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly long Demand;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly IActorSubscription Subscription;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="subscription">TBD</param>
    /// <param name="demand">TBD</param>
    public RequestMore(IActorSubscription subscription, long demand)
    {
        Subscription = subscription;
        Demand = demand;
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
internal sealed class Cancel : IDeadLetterSuppression
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly IActorSubscription Subscription;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="subscription">TBD</param>
    public Cancel(IActorSubscription subscription)
    {
        Subscription = subscription;
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
internal sealed class ExposedPublisher : IDeadLetterSuppression
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly IActorPublisher Publisher;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="publisher">TBD</param>
    public ExposedPublisher(IActorPublisher publisher)
    {
        Publisher = publisher;
    }
}

/// <summary>
///     TBD
/// </summary>
[Serializable]
public class NormalShutdownException : IllegalStateException
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="NormalShutdownException" /> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public NormalShutdownException(string message) : base(message)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="NormalShutdownException" /> class.
    /// </summary>
    /// <param name="info">
    ///     The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being
    ///     thrown.
    /// </param>
    /// <param name="context">
    ///     The <see cref="StreamingContext" /> that contains contextual information about the source or
    ///     destination.
    /// </param>
    protected NormalShutdownException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
}

/// <summary>
///     TBD
/// </summary>
public interface IActorPublisher : IUntypedPublisher
{
    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="reason">TBD</param>
    void Shutdown(Exception reason);

    /// <summary>
    ///     TBD
    /// </summary>
    /// <returns>TBD</returns>
    IEnumerable<IUntypedSubscriber> TakePendingSubscribers();
}

/// <summary>
///     TBD
/// </summary>
public static class ActorPublisher
{
    /// <summary>
    ///     TBD
    /// </summary>
    public const string NormalShutdownReasonMessage = "Cannot subscribe to shut-down Publisher";

    /// <summary>
    ///     TBD
    /// </summary>
    public static readonly NormalShutdownException NormalShutdownReason = new(NormalShutdownReasonMessage);
}

/// <summary>
///     INTERNAL API
///     When you instantiate this class, or its subclasses, you MUST send an ExposedPublisher message to the wrapped
///     ActorRef! If you don't need to subclass, prefer the apply() method on the companion object which takes care of
///     this.
/// </summary>
/// <typeparam name="TOut">TBD</typeparam>
[InternalApi]
public class ActorPublisher<TOut> : IActorPublisher, IPublisher<TOut>
{
    // The subscriber of an subscription attempt is first placed in this list of pending subscribers.
    // The actor will call takePendingSubscribers to remove it from the list when it has received the
    // SubscribePending message. The AtomicReference is set to null by the shutdown method, which is
    // called by the actor from postStop. Pending (unregistered) subscription attempts are denied by
    // the shutdown method. Subscription attempts after shutdown can be denied immediately.
    private readonly AtomicReference<ImmutableList<ISubscriber<TOut>>> _pendingSubscribers =
        new(ImmutableList<ISubscriber<TOut>>.Empty);

    /// <summary>
    ///     TBD
    /// </summary>
    protected readonly IActorRef Impl;

    private volatile Exception _shutdownReason;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="impl">TBD</param>
    public ActorPublisher(IActorRef impl)
    {
        Impl = impl;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    protected virtual object WakeUpMessage => SubscribePending.Instance;

    void IUntypedPublisher.Subscribe(IUntypedSubscriber subscriber)
    {
        Subscribe(UntypedSubscriber.ToTyped<TOut>(subscriber));
    }

    IEnumerable<IUntypedSubscriber> IActorPublisher.TakePendingSubscribers()
    {
        return TakePendingSubscribers().Select(UntypedSubscriber.FromTyped);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="reason">TBD</param>
    public void Shutdown(Exception reason)
    {
        _shutdownReason = reason;
        var pending = _pendingSubscribers.GetAndSet(null);
        if (pending != null)
            foreach (var subscriber in pending.Reverse())
                ReportSubscribeFailure(subscriber);
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="subscriber">TBD</param>
    /// <exception cref="ArgumentNullException">TBD</exception>
    public void Subscribe(ISubscriber<TOut> subscriber)
    {
        if (subscriber == null) throw new ArgumentNullException(nameof(subscriber));
        while (true)
        {
            var current = _pendingSubscribers.Value;
            if (current == null)
            {
                ReportSubscribeFailure(subscriber);
                break;
            }

            if (_pendingSubscribers.CompareAndSet(current, current.Add(subscriber)))
            {
                Impl.Tell(WakeUpMessage);
                break;
            }
        }
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <returns>TBD</returns>
    public IEnumerable<ISubscriber<TOut>> TakePendingSubscribers()
    {
        var pending = _pendingSubscribers.GetAndSet(ImmutableList<ISubscriber<TOut>>.Empty);
        return pending ?? ImmutableList<ISubscriber<TOut>>.Empty;
    }

    private void ReportSubscribeFailure(ISubscriber<TOut> subscriber)
    {
        try
        {
            if (_shutdownReason == null)
            {
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnComplete(subscriber);
            }
            else if (_shutdownReason is ISpecViolation)
            {
                // ok, not allowed to call OnError
            }
            else
            {
                ReactiveStreamsCompliance.TryOnSubscribe(subscriber, CancelledSubscription.Instance);
                ReactiveStreamsCompliance.TryOnError(subscriber, _shutdownReason);
            }
        }
        catch (Exception exception)
        {
            if (!(exception is ISpecViolation))
                throw;
        }
    }
}

/// <summary>
///     TBD
/// </summary>
public interface IActorSubscription : ISubscription
{
}

/// <summary>
///     TBD
/// </summary>
public static class ActorSubscription
{
    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="implementor">TBD</param>
    /// <param name="subscriber">TBD</param>
    /// <returns>TBD</returns>
    internal static IActorSubscription Create(IActorRef implementor, IUntypedSubscriber subscriber)
    {
        var subscribedType =
            subscriber.GetType().GetGenericArguments().First(); // assumes type is UntypedSubscriberWrapper
        var subscriptionType = typeof(ActorSubscription<>).MakeGenericType(subscribedType);
        return (IActorSubscription)Activator.CreateInstance(subscriptionType, implementor,
            UntypedSubscriber.ToTyped(subscriber));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    /// <param name="implementor">TBD</param>
    /// <param name="subscriber">TBD</param>
    /// <returns>TBD</returns>
    public static IActorSubscription Create<T>(IActorRef implementor, ISubscriber<T> subscriber)
    {
        return new ActorSubscription<T>(implementor, subscriber);
    }
}

/// <summary>
///     TBD
/// </summary>
/// <typeparam name="T">TBD</typeparam>
public class ActorSubscription<T> : IActorSubscription
{
    /// <summary>
    ///     TBD
    /// </summary>
    public readonly IActorRef Implementor;

    /// <summary>
    ///     TBD
    /// </summary>
    public readonly ISubscriber<T> Subscriber;

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="implementor">TBD</param>
    /// <param name="subscriber">TBD</param>
    public ActorSubscription(IActorRef implementor, ISubscriber<T> subscriber)
    {
        Implementor = implementor;
        Subscriber = subscriber;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="n">TBD</param>
    public void Request(long n)
    {
        Implementor.Tell(new RequestMore(this, n));
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public void Cancel()
    {
        Implementor.Tell(new Cancel(this));
    }
}

/// <summary>
///     TBD
/// </summary>
/// <typeparam name="TIn">TBD</typeparam>
public class ActorSubscriptionWithCursor<TIn> : ActorSubscription<TIn>, ISubscriptionWithCursor<TIn>
{
    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="implementor">TBD</param>
    /// <param name="subscriber">TBD</param>
    public ActorSubscriptionWithCursor(IActorRef implementor, ISubscriber<TIn> subscriber) : base(implementor,
        subscriber)
    {
        IsActive = true;
        TotalDemand = 0;
        Cursor = 0;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     TBD
    /// </summary>
    public long Cursor { get; private set; }

    /// <summary>
    ///     TBD
    /// </summary>
    public long TotalDemand { get; private set; }

    ISubscriber<TIn> ISubscriptionWithCursor<TIn>.Subscriber => Subscriber;

    bool ISubscriptionWithCursor<TIn>.IsActive
    {
        get => IsActive;
        set => IsActive = value;
    }

    long ISubscriptionWithCursor<TIn>.TotalDemand
    {
        get => TotalDemand;
        set => TotalDemand = value;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="element">TBD</param>
    public void Dispatch(TIn element)
    {
        ReactiveStreamsCompliance.TryOnNext(Subscriber, element);
    }

    long ICursor.Cursor
    {
        get => Cursor;
        set => Cursor = value;
    }

    /// <summary>
    ///     TBD
    /// </summary>
    /// <param name="element">TBD</param>
    public void Dispatch(object element)
    {
        ReactiveStreamsCompliance.TryOnNext(Subscriber, (TIn)element);
    }
}