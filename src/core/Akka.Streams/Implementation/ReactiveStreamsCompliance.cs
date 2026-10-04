//-----------------------------------------------------------------------
// <copyright file="ReactiveStreamsCompliance.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;
using Akka.Pattern;
using Reactive.Streams;

namespace Akka.Streams.Implementation
{
    /// <summary>
    /// Marks failures that represent violations of the Reactive Streams protocol.
    /// </summary>
    public interface ISpecViolation { }

    /// <summary>
    /// Indicates that a subscriber threw while receiving a Reactive Streams signal.
    /// </summary>
    [Serializable]
    public class SignalThrewException : IllegalStateException, ISpecViolation
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SignalThrewException"/> class.
        /// </summary>
        /// <param name="message">The message that describes the error.</param>
        /// <param name="cause">The exception that is the cause of the current exception.</param>
        public SignalThrewException(string message, Exception cause) : base(message, cause) { }

        /// <summary>
        /// Initializes a new instance of the <see cref="SignalThrewException"/> class.
        /// </summary>
        /// <param name="info">The <see cref="SerializationInfo" /> that holds the serialized object data about the exception being thrown.</param>
        /// <param name="context">The <see cref="StreamingContext" /> that contains contextual information about the source or destination.</param>
        protected SignalThrewException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }

    /// <summary>
    /// Shared messages describing Reactive Streams protocol requirements and failures.
    /// </summary>
    public static class ReactiveStreamsCompliance
    {
        /// <summary>
        /// Gets the rejection message for subscribing the same subscriber more than once.
        /// </summary>
        public const string CanNotSubscribeTheSameSubscriberMultipleTimes =
            "can not subscribe the same subscriber multiple times (see reactive-streams specification, rules 1.10 and 2.12)";
        /// <summary>
        /// Gets the rejection message used when a publisher permits only one subscriber.
        /// </summary>
        public const string SupportsOnlyASingleSubscriber =
            "only supports one subscriber (which is allowed, see reactive-streams specification, rule 1.12)";
        /// <summary>
        /// Gets the message for a request whose element count is not positive.
        /// </summary>
        public const string NumberOfElementsInRequestMustBePositiveMsg =
            "The number of requested elements must be > 0 (see reactive-streams specification, rule 3.9)";
        /// <summary>
        /// Gets the message used when a subscriber argument is null.
        /// </summary>
        public const string SubscriberMustNotBeNullMsg = "Subscriber must not be null, rule 1.9";
        /// <summary>
        /// Gets the message used when an error signal is null.
        /// </summary>
        public const string ExceptionMustNotBeNullMsg = "Exception must not be null, rule 2.13";
        /// <summary>
        /// Gets the message used when an element is null.
        /// </summary>
        public const string ElementMustNotBeNullMsg = "Element must not be null, rule 2.13";
        /// <summary>
        /// Gets the message used when a subscription is null.
        /// </summary>
        public const string SubscriptionMustNotBeNullMsg = "Subscription must not be null, rule 2.13";

        /// <summary>
        /// Gets the exception shared for requests with non-positive demand.
        /// </summary>
        public static readonly Exception NumberOfElementsInRequestMustBePositiveException =
            new ArgumentException(NumberOfElementsInRequestMustBePositiveMsg);
        /// <summary>
        /// Gets the exception shared when the same subscriber is subscribed more than once.
        /// </summary>
        public static readonly Exception CanNotSubscribeTheSameSubscriberMultipleTimesException =
            new IllegalStateException(CanNotSubscribeTheSameSubscriberMultipleTimes);

        /// <summary>
        /// Gets the exception shared when an element is null.
        /// </summary>
        public static readonly Exception ElementMustNotBeNullException =
            new ArgumentNullException("element", ElementMustNotBeNullMsg);
        /// <summary>
        /// Gets the exception shared when a subscription is null.
        /// </summary>
        public static readonly Exception SubscriptionMustNotBeNullException =
            new ArgumentNullException("subscription", SubscriptionMustNotBeNullMsg);

        /// <summary>
        /// Gets the exception shared when a subscriber is null.
        /// </summary>
        public static Exception SubscriberMustNotBeNullException { get; } = new ArgumentNullException("subscriber", SubscriberMustNotBeNullMsg);

        /// <summary>
        /// Gets the exception shared when an error cause is null.
        /// </summary>
        public static Exception ExceptionMustNotBeNullException { get; } = new ArgumentNullException("exception", ExceptionMustNotBeNullMsg);

        /// <summary>
        /// Calls <see cref="ISubscriber{T}.OnSubscribe"/> and wraps subscriber exceptions as a protocol violation.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber receiving the subscription.</param>
        /// <param name="subscription">The subscription to deliver.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling <see cref="ISubscriber{T}.OnSubscribe"/>.</exception>
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

        /// <summary>
        /// Calls the untyped subscriber's OnSubscribe callback and wraps any thrown exception.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving the subscription.</param>
        /// <param name="subscription">The subscription to deliver.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling OnSubscribe.</exception>
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

        /// <summary>
        /// Validates and delivers an element to a subscriber, wrapping exceptions thrown by its callback.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber receiving the element.</param>
        /// <param name="element">The element to deliver; it must not be null.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling <see cref="ISubscriber{T}.OnNext"/>.</exception>
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

        /// <summary>
        /// Validates and delivers an untyped element, wrapping exceptions thrown by the callback.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving the element.</param>
        /// <param name="element">The element to deliver; it must not be null.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling OnNext.</exception>
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

        /// <summary>
        /// Delivers a failure unless it is itself a protocol violation, wrapping exceptions thrown by the callback.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber receiving the failure.</param>
        /// <param name="cause">The failure to deliver.</param>
        /// <exception cref="IllegalStateException">The cause is marked as a Reactive Streams specification violation.</exception>
        /// <exception cref="SignalThrewException">The subscriber throws while handling <see cref="ISubscriber{T}.OnError"/>.</exception>
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

        /// <summary>
        /// Delivers a failure to an untyped subscriber unless it is itself a protocol violation.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving the failure.</param>
        /// <param name="cause">The failure to deliver.</param>
        /// <exception cref="IllegalStateException">The cause is marked as a Reactive Streams specification violation.</exception>
        /// <exception cref="SignalThrewException">The subscriber throws while handling OnError.</exception>
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

        /// <summary>
        /// Delivers successful completion, wrapping exceptions thrown by the callback.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber receiving completion.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling <see cref="ISubscriber{T}.OnComplete"/>.</exception>
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

        /// <summary>
        /// Delivers successful completion to an untyped subscriber and wraps exceptions thrown by the callback.
        /// </summary>
        /// <param name="subscriber">The subscriber receiving completion.</param>
        /// <exception cref="SignalThrewException">The subscriber throws while handling OnComplete.</exception>
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

        /// <summary>
        /// Signals the standard duplicate-subscription failure to a subscriber already subscribed.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber to notify of the duplicate subscription.</param>
        public static void RejectDuplicateSubscriber<T>(ISubscriber<T> subscriber)
        {
            // since it is already subscribed it has received the subscription first
            // and we can emit onError immediately
            TryOnError(subscriber, CanNotSubscribeTheSameSubscriberMultipleTimesException);
        }

        /// <summary>
        /// Gives an additional subscriber a cancelled subscription and signals that only one subscriber is supported.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The additional subscriber to reject.</param>
        /// <param name="rejector">The publisher name or context included in the rejection message.</param>
        public static void RejectAdditionalSubscriber<T>(ISubscriber<T> subscriber, string rejector)
        {
            TryOnSubscribe(subscriber, CancelledSubscription.Instance);
            TryOnError(subscriber, new IllegalStateException(rejector + " " + SupportsOnlyASingleSubscriber));
        }

        /// <summary>
        /// Gives an additional untyped subscriber a cancelled subscription and signals that only one subscriber is supported.
        /// </summary>
        /// <param name="subscriber">The additional subscriber to reject.</param>
        /// <param name="rejector">The publisher name or context included in the rejection message.</param>
        internal static void RejectAdditionalSubscriber(IUntypedSubscriber subscriber, string rejector)
        {
            TryOnSubscribe(subscriber, CancelledSubscription.Instance);
            TryOnError(subscriber, new IllegalStateException(rejector + " " + SupportsOnlyASingleSubscriber));
        }

        /// <summary>
        /// Signals the Reactive Streams error for a request with non-positive demand.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber receiving the demand error.</param>
        public static void RejectDueToNonPositiveDemand<T>(ISubscriber<T> subscriber)
        {
            TryOnError(subscriber, NumberOfElementsInRequestMustBePositiveException);
        }

        /// <summary>
        /// Throws the shared protocol exception when the subscriber is null.
        /// </summary>
        /// <typeparam name="T">The subscriber's element type.</typeparam>
        /// <param name="subscriber">The subscriber to validate.</param>
        public static void RequireNonNullSubscriber<T>(ISubscriber<T> subscriber)
        {
            if (ReferenceEquals(subscriber, null))
                throw SubscriberMustNotBeNullException;
        }

        /// <summary>
        /// Throws the shared protocol exception when the subscription is null.
        /// </summary>
        /// <param name="subscription">The subscription to validate.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="subscription"/> is undefined.
        /// </exception>
        public static void RequireNonNullSubscription(ISubscription subscription)
        {
            if (ReferenceEquals(subscription, null))
                throw SubscriptionMustNotBeNullException;
        }

        /// <summary>
        /// Throws the shared protocol exception when the failure is null.
        /// </summary>
        /// <param name="exception">The failure to validate.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="exception"/> is undefined.
        /// </exception>
        public static void RequireNonNullException(Exception exception)
        {
            if (ReferenceEquals(exception, null))
                throw ExceptionMustNotBeNullException;
        }

        /// <summary>
        /// Throws the shared protocol exception when the element is null.
        /// </summary>
        /// <param name="element">The element to validate.</param>
        /// <exception cref="ArgumentNullException">
        /// This exception is thrown when the specified <paramref name="element"/> is undefined.
        /// </exception>
        public static void RequireNonNullElement(object element)
        {
            if (ReferenceEquals(element, null))
                throw ElementMustNotBeNullException;
        }

        /// <summary>
        /// Cancels a subscription, passing the cause when it supports cancellation with an exception.
        /// </summary>
        /// <param name="subscription">The subscription to cancel.</param>
        /// <param name="cause">The cancellation cause supplied to subscriptions that support it.</param>
        /// <exception cref="SignalThrewException">
        /// This exception is thrown when an exception occurs while canceling the specified <paramref name="subscription"/>.
        /// </exception>
        public static void TryCancel(ISubscription subscription, Exception cause)
        {
            if (subscription == null)
                throw new IllegalStateException("Subscription must be not null on cancel() call, rule 1.3");
            
            try
            {
                if (subscription is ISubscriptionWithCancelException s)
                {
                    s.Cancel(cause);
                }
                else
                {
                    subscription.Cancel();
                }
            }
            catch (Exception e)
            {
                throw new SignalThrewException("It is illegal to throw exceptions from cancel(), rule 3.15", e);
            }
        }

        /// <summary>
        /// Requests the specified number of elements, wrapping exceptions thrown by the subscription.
        /// </summary>
        /// <param name="subscription">The subscription to request from.</param>
        /// <param name="demand">The number of elements to request.</param>
        /// <exception cref="SignalThrewException">
        /// This exception is thrown when an exception occurs while requesting no events be sent to the specified <paramref name="subscription"/>.
        /// </exception>
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
