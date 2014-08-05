using System;
using System.Runtime.Remoting.Contexts;
using Akka.Actor;
using Akka.Dispatch;
using Akka.Dispatch.SysMsg;
using Akka.Event;
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
            terminal.Tell(PoisonPill.Instance,testActor);
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
        [Fact]
        public void Bug209_any_user_messages_following_a_Terminate_message_should_be_forwarded_to_DeadLetterMailbox()
        {
            var actor = (LocalActorRef)System.ActorOf(Props.Empty, "killed-actor");

            sys.EventStream.Subscribe(testActor, typeof(DeadLetter));

            var mailbox = actor.Cell.Mailbox;
            //Wait for the mailbox to become idle after processed all initial messages.
            AwaitCond(() =>
                !mailbox.HasUnscheduledMessages && mailbox.Status == Mailbox.MailboxStatus.Idle,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromMilliseconds(50));

            //Suspend the mailbox and post Terminate and a user message
            mailbox.Suspend();
            mailbox.Post(new Envelope() { Message = Terminate.Instance, Sender = testActor });
            mailbox.Post(new Envelope() { Message = "SomeUserMessage", Sender = testActor });

            //Resume the mailbox, which will also schedule
            mailbox.Resume();

            //The actor should Terminate, exchange the mailbox to a DeadLetterMailbox and forward the user message to the DeadLetterMailbox
            ExpectMsgPF<DeadLetter>(TimeSpan.FromSeconds(1),d=>(string) d.Message=="SomeUserMessage");
            actor.Cell.Mailbox.ShouldBe(System.Mailboxes.DeadLetterMailbox);            
        }


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
                    _forwardToActor.Forward(new WrappedTerminated(terminated));
                else
                    _forwardToActor.Forward(message);
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