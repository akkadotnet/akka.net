//-----------------------------------------------------------------------
// <copyright file="UntypedPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams
{
    internal interface IUntypedPublisher
    {
        void Subscribe(IUntypedSubscriber subscriber);
    }

    internal abstract class UntypedPublisher : IUntypedPublisher
    {
        public abstract void Subscribe(IUntypedSubscriber subscriber);

        public abstract object Unwrap();

        public static UntypedPublisher FromTyped(object publisher)
        {
            var publishedType = publisher.GetType().GetPublishedType();
            return (UntypedPublisher) typeof(UntypedPublisherImpl<>).Instantiate(publishedType, publisher);
        }

        public static UntypedPublisher FromTyped<T>(IPublisher<T> publisher)
        {
            return new UntypedPublisherImpl<T>(publisher);
        }

        public static object ToTyped(IUntypedPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedPublisher)
                return ((UntypedPublisher) untypedPublisher).Unwrap();
            return untypedPublisher;
        }

        public static IPublisher<T> ToTyped<T>(IUntypedPublisher untypedPublisher)
        {
            return (IPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    internal sealed class UntypedPublisherImpl<T> : UntypedPublisher
    {
        private readonly IPublisher<T> _publisher;

        public UntypedPublisherImpl(IPublisher<T> publisher)
        {
            _publisher = publisher;
        }

        public override void Subscribe(IUntypedSubscriber subscriber)
        {
            _publisher.Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));
        }

        public override object Unwrap()
        {
            return _publisher;
        }

        public override string ToString()
        {
            return _publisher.ToString();
        }
    }
}