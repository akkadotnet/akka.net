//-----------------------------------------------------------------------
// <copyright file="UntypedPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2015-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Akka.Streams.Util;

namespace Akka.Streams
{
    internal interface IUntypedVirtualPublisher
    {
        void Subscribe(IUntypedSubscriber subscriber);
        void RegisterPublisher(IUntypedPublisher publisher);
    }

    internal abstract class UntypedVirtualPublisher : IUntypedVirtualPublisher
    {
        public abstract void Subscribe(IUntypedSubscriber subscriber);
        public abstract void RegisterPublisher(IUntypedPublisher publisher);

        public abstract object Unwrap();

        public static UntypedVirtualPublisher FromTyped(object publisher)
        {
            var publishedType = publisher.GetType().GetPublishedType();
            return (UntypedVirtualPublisher) typeof(UntypedPublisherImpl<>).Instantiate(publishedType, publisher);
        }

        public static UntypedVirtualPublisher FromTyped<T>(VirtualPublisher<T> publisher)
        {
            return new UntypedVirtualPublisherImpl<T>(publisher);
        }

        public static object ToTyped(IUntypedVirtualPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedVirtualPublisher)
                return ((UntypedVirtualPublisher) untypedPublisher).Unwrap();
            return untypedPublisher;
        }

        public static VirtualPublisher<T> ToTyped<T>(IUntypedVirtualPublisher untypedPublisher)
        {
            return (VirtualPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    internal sealed class UntypedVirtualPublisherImpl<T> : UntypedVirtualPublisher
    {
        private readonly VirtualPublisher<T> _publisher;

        public UntypedVirtualPublisherImpl(VirtualPublisher<T> publisher)
        {
            _publisher = publisher;
        }

        public override void Subscribe(IUntypedSubscriber subscriber)
        {
            _publisher.Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));
        }

        public override void RegisterPublisher(IUntypedPublisher publisher)
        {
            _publisher.RegisterPublisher(UntypedPublisher.ToTyped<T>(publisher));
        }

        public override object Unwrap() => _publisher;

        public override string ToString() => _publisher.ToString();
    }
}