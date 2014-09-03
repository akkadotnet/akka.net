using Akka.Actor;

namespace Akka.TestKit
{
    public interface ImplicitSender
    {
        ActorRef Self { get; }
    }
}