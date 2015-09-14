namespace System.Reactive.Streams
{
    public interface IProcessor<in T1, out T2> : ISubscriber<T1>, IPublisher<T2>
    {
    }
}