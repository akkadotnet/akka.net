namespace System.Reactive.Streams
{
    /// <summary>
    /// Will receive call to <see cref="OnSubscribe"/> once after passing an instance of <see cref="ISubscriber{T}"/> to <see cref="IPublisher{T}.Subscribe"/>.
    /// <para>No further notifications will be received until <see cref="ISubscription.Request"/> is called.</para>
    ///  
    /// <para>After signaling demand: </para>
    /// <para>1. One or more invocations of <see cref="OnNext"/> up to the maximum number defined by <see cref="ISubscription.Request"/></para>
    /// <para>2. Single invocation of <see cref="OnError"/> or <see cref="OnComplete"/> which signals a terminal state after which no further events will be sent.</para>
    /// <para>Demand can be signaled via <see cref="ISubscription.Request"/> whenever the <see cref="ISubscriber{T}"/> instance is capable of handling more.</para>
    ///  </summary>
    /// <typeparam name="T">The type of element signaled.</typeparam>
    public interface ISubscriber<in T> : ISubscriber
    {
        /// <summary>
        /// Data notification sent by the <see cref="IPublisher{T}"/> in response to requests to <see cref="ISubscription.Request"/>.
        /// </summary>
        /// <param name="element">The element signaled</param>
        void OnNext(T element);
    }

    public interface ISubscriber
    {
        void OnSubscribe(ISubscription subscription);
        void OnError(Exception cause);
        void OnComplete();
        void OnNext(object element);
    }
}