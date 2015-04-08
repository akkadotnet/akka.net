using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WorkerActor : TActorBase
    {
        protected override bool ReceiveMessage(object message)
        {
            if((message as string) == "work")
            {
                Sender.Tell("workDone");
                Context.Stop(Self);
                return true;

            }
            //TODO: case replyTo: Promise[_] ⇒ replyTo.asInstanceOf[Promise[Any]].success("complexReply")
            if(message is IActorRef)
            {
                ((IActorRef)message).Tell("complexReply", Self);
                return true;
            }
            return false;
        }
    }
}