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
    public interface ISubscriber<in T>
    {
        /// <summary>
        /// Invoked after calling <see cref="IPublisher{T}.Subscribe"/>.
        /// No data will start flowing until <see cref="ISubscription.Request"/> is invoked.
        /// It is the responsibility of this <see cref="ISubscriber{T}"/> instance to call <see cref="ISubscription.Request"/> whenever more data is wanted.
        /// The <see cref="IPublisher{T}"/> will send notifications only in response to <see cref="ISubscription.Request"/>.
        /// </summary>
        /// <param name="subscription"><see cref="ISubscription"/> that allows requesting data via <see cref="ISubscription.Request"/></param>
        void OnSubscribe(ISubscription subscription);

        /// <summary>
        /// Failed terminal state.
        /// No further events will be sent even if <see cref="ISubscription.Request"/> is invoked again.
        /// </summary>
        /// <param name="cause">The exception signaled</param>
        void OnError(Exception cause);

        /// <summary>
        /// Successful terminal state.
        /// No further events will be sent even if <see cref="ISubscription.Request"/> is invoked again.
        /// </summary>
        void OnComplete();

        /// <summary>
        /// Data notification sent by the <see cref="IPublisher{T}"/> in response to requests to <see cref="ISubscription.Request"/>.
        /// </summary>
        /// <param name="element">The element signaled</param>
        void OnNext(T element);
    }
}