using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class NestingActor : ActorBase
    {
        private readonly ActorRef _nested;

        public NestingActor(bool createTestActorRef)
        {
            _nested = createTestActorRef ? Context.System.ActorOf<NestedActor>() : new TestActorRef<NestedActor>(Context.System, Props.Create<NestedActor>(), null, null);
        }

        protected override bool Receive(object message)
        {
            Sender.Tell(_nested, Self);
            return true;
        }

        private class NestedActor : ActorBase
        {
            protected override bool Receive(object message)
            {
                return true;
            }
        }
    }
}