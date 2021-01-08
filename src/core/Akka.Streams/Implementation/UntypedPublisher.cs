//-----------------------------------------------------------------------
// <copyright file="UntypedPublisher.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Streams.Util;
using Reactive.Streams;

namespace Akka.Streams
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface IUntypedPublisher
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        void Subscribe(IUntypedSubscriber subscriber);
    }

    /// <summary>
    /// TBD
    /// </summary>
    internal abstract class UntypedPublisher : IUntypedPublisher
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="subscriber">TBD</param>
        public abstract void Subscribe(IUntypedSubscriber subscriber);

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
        public static UntypedPublisher FromTyped(object publisher)
        {
            var publishedType = publisher.GetType().GetPublishedType();
            return (UntypedPublisher) typeof(UntypedPublisherImpl<>).Instantiate(publishedType, publisher);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="publisher">TBD</param>
        /// <returns>TBD</returns>
        public static UntypedPublisher FromTyped<T>(IPublisher<T> publisher)
        {
            return new UntypedPublisherImpl<T>(publisher);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="untypedPublisher">TBD</param>
        /// <returns>TBD</returns>
        public static object ToTyped(IUntypedPublisher untypedPublisher)
        {
            if (untypedPublisher is UntypedPublisher)
                return ((UntypedPublisher) untypedPublisher).Unwrap();
            return untypedPublisher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <typeparam name="T">TBD</typeparam>
        /// <param name="untypedPublisher">TBD</param>
        /// <returns>TBD</returns>
        public static IPublisher<T> ToTyped<T>(IUntypedPublisher untypedPublisher)
        {
            return (IPublisher<T>) ToTyped(untypedPublisher);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    /// <typeparam name="T">TBD</typeparam>
    internal sealed class UntypedPublisherImpl<T> : UntypedPublisher
    {
        private readonly IPublisher<T> _publisher;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="publisher">TBD</param>
        public UntypedPublisherImpl(IPublisher<T> publisher)
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
        /// <returns>TBD</returns>
        public override object Unwrap()
        {
            return _publisher;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <returns>TBD</returns>
        public override string ToString()
        {
            return _publisher.ToString();
        }
    }
}
