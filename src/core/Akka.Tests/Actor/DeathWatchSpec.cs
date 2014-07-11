using System;
using Akka.Actor;
using Xunit;

namespace Akka.Tests.Actor
{
    public class DeathWatchSpec : AkkaSpec
    {
        private readonly InternalActorRef _supervisor;

        public DeathWatchSpec()
        {
            _supervisor = System.ActorOf(Props.Create<Supervisor>(() => new Supervisor(SupervisorStrategy.DefaultStrategy)), "watchers");
        }

        [Fact]
        public void Given_terminated_actor_When_watching_Then_should_receive_Terminated_message()
        {
            var terminal=System.ActorOf(Props.Empty,"killed-actor");
            terminal.Tell(new PoisonPill(),testActor);
            StartWatching(terminal);
            ExpectTerminationOf(terminal);
        }

//        protected override string GetConfig()
//        {
//            return @"
//                akka.log-dead-letters-during-shutdown = true
//                akka.actor.debug.autoreceive = true
//                akka.actor.debug.lifecycle = true
//                akka.actor.debug.event-stream = true
//                akka.actor.debug.unhandled = true
//                akka.log-dead-letters = true
//                akka.loglevel = DEBUG
//                akka.stdout-loglevel = DEBUG
//            ";
//        }

        private void ExpectTerminationOf(ActorRef actorRef)
        {
            ExpectMsgPF<WrappedTerminated>(TimeSpan.FromSeconds(5), w => ReferenceEquals(w.Terminated.ActorRef, actorRef));
        }

        private void StartWatching(ActorRef target)
        {
            _supervisor.Ask(CreateWatchAndForwarderProps(target, testActor), TimeSpan.FromSeconds(3));
        }

        private Props CreateWatchAndForwarderProps(ActorRef target, ActorRef forwardToActor)
        {
            return Props.Create(() => new WatchAndForwardActor(target, forwardToActor));
        }

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

        public class WrappedTerminated
        {
            private readonly Terminated _terminated;

            public WrappedTerminated(Terminated terminated)
            {
                _terminated = terminated;
            }

            public Terminated Terminated { get { return _terminated; } }
        }
    }
}