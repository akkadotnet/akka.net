using Akka.Actor;

namespace Akka.Event
{
    public class DeadLetter
    {
        public DeadLetter(object message, ActorRef sender, ActorRef recipient)
        {
            Message = message;
            Sender = sender;
            Recipient = recipient;
        }

        public object Message { get; private set; }

        public ActorRef Recipient { get; private set; }

        public ActorRef Sender { get; private set; }
    }
}