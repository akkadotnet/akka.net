using Akka.Actor;

namespace Akka.Tests.Serialization
{
    public class SomeMessage
    {
        public IActorRef ActorRef { get; set; }
    }
}