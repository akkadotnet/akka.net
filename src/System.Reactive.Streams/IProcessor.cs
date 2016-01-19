namespace System.Reactive.Streams
{
    /// <summary>
    /// A Processor represents a processing stage—which is both a <see cref="ISubscriber{T}"/>
    /// and a <see cref="IPublisher{T}"/> and obeys the contracts of both.
    /// </summary>
    /// <typeparam name="T1">The type of element signaled to the <see cref="ISubscriber{T}"/></typeparam>
    /// <typeparam name="T2">The type of element signaled to the <see cref="IPublisher{T}"/></typeparam>
    public interface IProcessor<in T1, out T2> : ISubscriber<T1>, IPublisher<T2>
    {
    }
}