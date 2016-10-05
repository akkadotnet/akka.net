//-----------------------------------------------------------------------
// <copyright file="ReactiveStreamsCompliance.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Pattern;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    public interface ISpecViolation { }

    [Serializable]
    public class SignalThrewException : IllegalStateException, ISpecViolation
    {
        public SignalThrewException(string message, Exception cause) : base(message, cause) { }
        protected SignalThrewException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    public static class ReactiveStreamsCompliance
    {
        public const string CanNotSubscribeTheSameSubscriberMultipleTimes =
            "can not subscribe the same subscriber multiple times (see reactive-streams specification, rules 1.10 and 2.12)";
        public const string SupportsOnlyASingleSubscriber =
            "only supports one subscriber (which is allowed, see reactive-streams specification, rule 1.12)";
        public const string NumberOfElementsInRequestMustBePositiveMsg =
            "The number of requested elements must be > 0 (see reactive-streams specification, rule 3.9)";
        public const string SubscriberMustNotBeNullMsg = "Subscriber must not be null, rule 1.9";
        public const string ExceptionMustNotBeNullMsg = "Exception must not be null, rule 2.13";
        public const string ElementMustNotBeNullMsg = "Element must not be null, rule 2.13";
        public const string SubscriptionMustNotBeNullMsg = "Subscription must not be null, rule 2.13";

        public static readonly Exception NumberOfElementsInRequestMustBePositiveException =
            new ArgumentException(NumberOfElementsInRequestMustBePositiveMsg);
        public static readonly Exception CanNotSubscribeTheSameSubscriberMultipleTimesException =
            new IllegalStateException(CanNotSubscribeTheSameSubscriberMultipleTimes);

        public static readonly Exception ElementMustNotBeNullException =
            new ArgumentNullException("element", ElementMustNotBeNullMsg);
        public static readonly Exception SubscriptionMustNotBeNullException =
            new ArgumentNullException("subscription", SubscriptionMustNotBeNullMsg);

        public static Exception SubscriberMustNotBeNullException { get; } = new ArgumentNullException("subscriber", SubscriberMustNotBeNullMsg);

        public static Exception ExceptionMustNotBeNullException { get; } = new ArgumentNullException("exception", ExceptionMustNotBeNullMsg);

        public static void TryOnSubscribe<T>(ISubscriber<T> subscriber, ISubscription subscription)
        {
            try
            {
                subscriber.OnSubscribe(subscription);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnSubscribe", e);
            }
        }

        internal static void TryOnSubscribe(IUntypedSubscriber subscriber, ISubscription subscription)
        {
            try
            {
                subscriber.OnSubscribe(subscription);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnSubscribe", e);
            }
        }

        public static void TryOnNext<T>(ISubscriber<T> subscriber, T element)
        {
            RequireNonNullElement(element);
            try
            {
                subscriber.OnNext(element);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnNext", e);
            }
        }

        internal static void TryOnNext(IUntypedSubscriber subscriber, object element)
        {
            RequireNonNullElement(element);
            try
            {
                subscriber.OnNext(element);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnNext", e);
            }
        }

        public static void TryOnError<T>(ISubscriber<T> subscriber, Exception cause)
        {
            if (cause is ISpecViolation)
                throw new IllegalStateException("It's illegal to try to signal OnError with a spec violation", cause);

            try
            {
                subscriber.OnError(cause);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnError", e);
            }
        }

        internal static void TryOnError(IUntypedSubscriber subscriber, Exception cause)
        {
            if (cause is ISpecViolation)
                throw new IllegalStateException("It's illegal to try to signal OnError with a spec violation", cause);

            try
            {
                subscriber.OnError(cause);
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnError", e);
            }
        }

        public static void TryOnComplete<T>(ISubscriber<T> subscriber)
        {
            try
            {
                subscriber.OnComplete();
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnComplete", e);
            }
        }

        internal static void TryOnComplete(IUntypedSubscriber subscriber)
        {
            try
            {
                subscriber.OnComplete();
            }
            catch (Exception e)
            {
                throw new SignalThrewException($"{subscriber}.OnComplete", e);
            }
        }

        public static void RejectDuplicateSubscriber<T>(ISubscriber<T> subscriber)
        {
            // since it is already subscribed it has received the subscription first
            // and we can emit onError immediately
            TryOnError(subscriber, CanNotSubscribeTheSameSubscriberMultipleTimesException);
        }

        public static void RejectAdditionalSubscriber<T>(ISubscriber<T> subscriber, string rejector)
        {
            TryOnSubscribe(subscriber, CancelledSubscription.Instance);
            TryOnError(subscriber, new IllegalStateException(rejector + " " + SupportsOnlyASingleSubscriber));
        }

        internal static void RejectAdditionalSubscriber(IUntypedSubscriber subscriber, string rejector)
        {
            TryOnSubscribe(subscriber, CancelledSubscription.Instance);
            TryOnError(subscriber, new IllegalStateException(rejector + " " + SupportsOnlyASingleSubscriber));
        }

        public static void RejectDueToNonPositiveDemand<T>(ISubscriber<T> subscriber)
        {
            TryOnError(subscriber, NumberOfElementsInRequestMustBePositiveException);
        }

        public static void RequireNonNullSubscriber<T>(ISubscriber<T> subscriber)
        {
            if (ReferenceEquals(subscriber, null))
                throw SubscriberMustNotBeNullException;
        }

        public static void RequireNonNullSubscription(ISubscription subscription)
        {
            if (ReferenceEquals(subscription, null))
                throw SubscriptionMustNotBeNullException;
        }

        public static void RequireNonNullException(Exception e)
        {
            if (ReferenceEquals(e, null))
                throw ExceptionMustNotBeNullException;
        }

        public static void RequireNonNullElement(object element)
        {
            if (ReferenceEquals(element, null))
                throw ElementMustNotBeNullException;
        }

        public static void TryCancel(ISubscription subscription)
        {
            try
            {
                subscription.Cancel();
            }
            catch (Exception e)
            {
                throw new SignalThrewException("It is illegal to throw exceptions from cancel(), rule 3.15", e);
            }
        }

        public static void TryRequest(ISubscription subscription, long demand)
        {
            try
            {
                subscription.Request(demand);
            }
            catch (Exception e)
            {
                throw new SignalThrewException("It is illegal to throw exceptions from request(), rule 3.16", e);
            }
        }
    }
}