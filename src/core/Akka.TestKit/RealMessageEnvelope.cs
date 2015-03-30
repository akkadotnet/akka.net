using Akka.Actor;

namespace Akka.TestKit
{
    public class RealMessageEnvelope : MessageEnvelope
    {
        private readonly object _message;
        private readonly IActorRef _sender;

        public RealMessageEnvelope(object message, IActorRef sender)
        {
            _message = message;
            _sender = sender;
        }

        public override object Message { get { return _message; } }
        public override IActorRef Sender{get { return _sender; }}

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender ?? NoSender.Instance);
        }
    }
}