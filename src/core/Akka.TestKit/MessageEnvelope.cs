using Akka.Actor;

namespace Akka.TestKit
{
    public abstract class MessageEnvelope   //this is called Message in Akka JVM
    {
        public abstract object Message { get; }

        public abstract ActorRef Sender { get; }
    }
}