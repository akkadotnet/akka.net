using System;
using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams
{
    internal interface IUntypedSubscriber
    {
        void OnSubscribe(ISubscription subscription);
        void OnNext(object element);
        void OnError(Exception cause);
        void OnComplete();
    }

    internal abstract class UntypedSubscriber : IUntypedSubscriber
    {
        public abstract void OnSubscribe(ISubscription subscription);

        public abstract void OnNext(object element);

        public abstract void OnError(Exception cause);

        public abstract void OnComplete();

        public abstract object Unwrap();

        public static UntypedSubscriber FromTyped(object subscriber)
        {
            var subscribedType = subscriber.GetType().GetSubscribedType();
            return (UntypedSubscriber) typeof(UntypedSubscriberImpl<>).Instantiate(subscribedType, subscriber);
        }

        public static UntypedSubscriber FromTyped<T>(ISubscriber<T> subscriber)
        {
            return new UntypedSubscriberImpl<T>(subscriber);
        }

        public static object ToTyped(IUntypedSubscriber untypedSubscriber)
        {
            if (untypedSubscriber is UntypedSubscriber)
                return ((UntypedSubscriber) untypedSubscriber).Unwrap();
            return untypedSubscriber;
        }

        public static ISubscriber<T> ToTyped<T>(IUntypedSubscriber untypedSubscriber)
        {
            return (ISubscriber<T>) ToTyped(untypedSubscriber);
        }
    }

    internal sealed class UntypedSubscriberImpl<T> : UntypedSubscriber
    {
        private readonly ISubscriber<T> _subscriber;

        public UntypedSubscriberImpl(ISubscriber<T> subscriber)
        {
            _subscriber = subscriber;
        }

        public override void OnSubscribe(ISubscription subscription)
        {
            _subscriber.OnSubscribe(subscription);
        }

        public override void OnNext(object element)
        {
            _subscriber.OnNext((T) element);
        }

        public override void OnError(Exception cause)
        {
            _subscriber.OnError(cause);
        }

        public override void OnComplete()
        {
            _subscriber.OnComplete();
        }

        public override object Unwrap()
        {
            return _subscriber;
        }

        public override string ToString()
        {
            return _subscriber.ToString();
        }
    }
}