using Akka.Actor;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class WatchAndForwardActor : ActorBase
    {
        private readonly ActorRef _forwardToActor;

        public WatchAndForwardActor(ActorRef watchedActor, ActorRef forwardToActor)
        {
            _forwardToActor = forwardToActor;
            Context.Watch(watchedActor);
        }

        protected override bool Receive(object message)
        {
            var terminated = message as Terminated;
            if(terminated != null)
                _forwardToActor.Tell(new WrappedTerminated(terminated), Sender);
            else
                _forwardToActor.Tell(message, Sender);
            return true;
        }
    }
}