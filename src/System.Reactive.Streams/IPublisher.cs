namespace System.Reactive.Streams
{
    public interface IPublisher<out T>
    {
        void Subscribe(ISubscriber<T> subscriber);
    }
}