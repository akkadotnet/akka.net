using Akka.Dispatch.MessageQueues;

namespace Akka.Dispatch
{
    public interface RequiresMessageQueue<T> where T:Semantics
    {
    }
}
