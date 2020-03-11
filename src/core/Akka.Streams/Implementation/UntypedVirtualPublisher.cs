//-----------------------------------------------------------------------
// <copyright file="UntypedVirtualPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Implementation;
using Akka.Streams.Util;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    internal interface IUntypedVirtualPublisher
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        void Subscribe(IUntypedSubscriber subscriber);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        void RegisterPublisher(IUntypedPublisher publisher);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class UntypedVirtualPublisher : IUntypedVirtualPublisher
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public abstract void Subscribe(IUntypedSubscriber subscriber);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        public abstract void RegisterPublisher(IUntypedPublisher publisher);

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public abstract object Unwrap();

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        /// <returns>TBD</returns>
        public static UntypedVirtualPublisher FromTyped(object publisher)
        {
            var publishedType = publisher.GetType().GetPublishedType();
            return (UntypedVirtualPublisher) typeof(UntypedPublisherImpl<>).Instantiate(publishedType, publisher);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="publisher">TBD</param>
        /// <returns>TBD</returns>
        public static UntypedVirtualPublisher FromTyped<T>(VirtualPublisher<T> publisher)
        {
            return new UntypedVirtualPublisherImpl<T>(publisher);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="untypedPublisher">TBD</param>
        /// <returns>TBD</returns>
        public static object ToTyped(IUntypedVirtualPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedVirtualPublisher)
                return ((UntypedVirtualPublisher) untypedPublisher).Unwrap();
            return untypedPublisher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="untypedPublisher">TBD</param>
        /// <returns>TBD</returns>
        public static VirtualPublisher<T> ToTyped<T>(IUntypedVirtualPublisher untypedPublisher)
        {
            return (VirtualPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class UntypedVirtualPublisherImpl<T> : UntypedVirtualPublisher
    {
        private readonly VirtualPublisher<T> _publisher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        public UntypedVirtualPublisherImpl(VirtualPublisher<T> publisher)
        {
            _publisher = publisher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public override void Subscribe(IUntypedSubscriber subscriber)
        {
            _publisher.Subscribe(UntypedSubscriber.ToTyped<T>(subscriber));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        public override void RegisterPublisher(IUntypedPublisher publisher)
        {
            _publisher.RegisterPublisher(UntypedPublisher.ToTyped<T>(publisher));
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override object Unwrap() => _publisher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString() => _publisher.ToString();
    }
}
