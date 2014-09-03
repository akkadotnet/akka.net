using Akka.Actor;

namespace Akka.TestKit
{
    public sealed class NullMessageEnvelope : MessageEnvelope
    {
        public static NullMessageEnvelope Instance=new NullMessageEnvelope();

        private NullMessageEnvelope(){}

        public override object Message
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        public override ActorRef Sender
        {
            get { throw new IllegalActorStateException("last receive did not dequeue a message"); }
        }

        public override string ToString()
        {
            return "<null>";
        }
    }
}