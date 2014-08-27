using Akka.Actor;

namespace Akka.Dispatch.MessageQueues
{
    public interface MessageQueue
    {
        void Enqueue(Envelope envelope);
        bool HasMessages { get; }
        int Count { get; }
        bool TryDequeue(out Envelope envelope);
    }
}
