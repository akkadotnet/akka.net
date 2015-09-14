namespace System.Reactive.Streams
{
    public interface ISubscriber<in T>
    {
        void OnSubscribe(ISubscription subscription);
        void OnError(Exception cause);
        void OnComplete();
        void OnNext(T element);
    }
}