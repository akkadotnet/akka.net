using Akka.Actor;

namespace Akka.TestKit
{
    public class RealMessageEnvelope : MessageEnvelope
    {
        private readonly object _message;
        private readonly ActorRef _sender;

        public RealMessageEnvelope(object message, ActorRef sender)
        {
            _message = message;
            _sender = sender;
        }

        public override object Message { get { return _message; } }
        public override ActorRef Sender{get { return _sender; }}

        public override string ToString()
        {
            return "<" + (Message ?? "null") + "> from " + (Sender ?? NoSender.Instance);
        }
    }
}