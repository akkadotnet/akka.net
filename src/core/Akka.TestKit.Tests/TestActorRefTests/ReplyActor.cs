using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class ReplyActor : TActorBase
    {
        private ActorRef _replyTo;

        protected override bool ReceiveMessage(object message)
        {
            var strMessage = message as string;
            switch(strMessage)
            {
                case "complexRequest":
                    _replyTo = Sender;
                    var worker = new TestActorRef<WorkerActor>(System, Props.Create<WorkerActor>());
                    worker.Tell("work");
                    return true;
                case "complexRequest2":
                    var worker2 = new TestActorRef<WorkerActor>(System, Props.Create<WorkerActor>());
                    worker2.Tell(Sender, Self);
                    return true;
                case "workDone":
                    _replyTo.Tell("complexReply", Self);
                    return true;
                case "simpleRequest":
                    Sender.Tell("simpleReply", Self);
                    return true;
            }
            return false;
        }
    }
}