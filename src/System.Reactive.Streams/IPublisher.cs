namespace System.Reactive.Streams
{
    /// <summary>
    /// A <see cref="IPublisher{T}"/> is a provider of a potentially unbounded number of sequenced elements, publishing them according to
    /// the demand received from its <see cref="ISubscriber{T}"/>.
    /// A <see cref="IPublisher{T}"/> can serve multiple <see cref="ISubscriber{T}"/>s subscribed <see cref="Subscribe"/> dynamically
    /// at various points in time.
    /// </summary>
    /// <typeparam name="T">The type of element signaled.</typeparam>
    public interface IPublisher<out T> : IPublisher
    {
        /// <summary>
        /// Request <see cref="IPublisher{T}"/> to start streaming data.
        /// This is a "factory method" and can be called multiple times, each time starting a new <see cref="ISubscription"/>.
        /// 
        /// Each <see cref="ISubscription"/> will work for only a single <see cref="ISubscriber{T}"/>.
        /// A <see cref="ISubscriber{T}"/> should only subscribe once to a single <see cref="IPublisher{T}"/>.
        /// 
        /// If the <see cref="IPublisher{T}"/> rejects the subscription attempt or otherwise fails it will
        /// signal the error via <see cref="ISubscriber{T}.OnError"/>.
        /// </summary>
        /// <param name="subscriber">the <see cref="ISubscriber{T}"/> that will consume signals from this <see cref="IPublisher{T}"/></param>
        void Subscribe(ISubscriber<T> subscriber);
    }

    public interface IPublisher
    {
        void Subscribe(ISubscriber subscriber);
    }
}