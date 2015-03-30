using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class SenderActor : TActorBase
    {
        private readonly IActorRef _replyActor;

        public SenderActor(IActorRef replyActor)
        {
            _replyActor = replyActor;
        }

        protected override bool ReceiveMessage(object message)
        {
            var strMessage = message as string;
            switch(strMessage)
            {
                case "complex":
                    _replyActor.Tell("complexRequest", Self);
                    return true;
                case "complex2":
                    _replyActor.Tell("complexRequest2", Self);
                    return true;
                case "simple":
                    _replyActor.Tell("simpleRequest", Self);
                    return true;
                case "complexReply":
                    TestActorRefSpec.Counter--;
                    return true;
                case "simpleReply":
                    TestActorRefSpec.Counter--;
                    return true;
            }
            return false;
        }
    }
}