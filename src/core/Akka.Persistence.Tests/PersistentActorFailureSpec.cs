using System;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{

    internal class FailingMemoryJournal : AsyncWriteProxy
    {
        private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

        protected override void PreStart()
        {
            base.PreStart();
            Self.Tell(new SetStore(Context.ActorOf(Props.Create<PersistentActorFailureSpec.FailingMemoryStore>())));
        }
    }

    public class PersistentActorFailureSpec : PersistenceSpec
    {
        #region internal test classes

        internal class FailingMemoryStore : MemoryStore
        {
            private bool FailingReceive(object message)
            {
                if (message is AsyncWriteTarget.ReplayMessages)
                {
                    var replay = message as AsyncWriteTarget.ReplayMessages;
                    var readFromStore = Read(replay.PersistenceId, replay.FromSequenceNr, replay.ToSequenceNr, replay.Max).ToArray();

                    if (readFromStore.Length == 0) Sender.Tell(AsyncWriteTarget.ReplaySuccess.Instance);
                    else Sender.Tell(new AsyncWriteTarget.ReplayFailure(new ArgumentException(
                            string.Format("yolo {0} {1}", replay.FromSequenceNr, replay.ToSequenceNr))));
                }
                else return false;
                return true;
            }

            protected override bool Receive(object message)
            {
                return FailingReceive(message) || base.Receive(message);
            }
        }

        internal class Supervisor : ActorBase
        {
            private readonly ActorRef _testActor;

            public Supervisor(ActorRef testActor)
            {
                _testActor = testActor;
            }

            protected override SupervisorStrategy SupervisorStrategy()
            {
                return new OneForOneStrategy(exception =>
                {
                    _testActor.Tell(exception);
                    return Directive.Stop;
                });
            }

            protected override bool Receive(object message)
            {
                var props = message as Props;
                var m = props != null ? Context.ActorOf(props) : message;
                Sender.Tell(m);
                return true;
            }
        }

        #endregion

        public PersistentActorFailureSpec()
            : base(Configuration("inmem", "PersistentActorFailureSpec",
                extraConfig: @"akka.persistence.journal.inmem.class = ""Akka.Persistence.Tests.FailingMemoryJournal, Akka.Persistence.Tests""
                    akka.actor.serialize-messages=off"))
        {
            //TODO: remove akka.actor.serialize-messages=off when Props serialization will be resolved (github issue: #569)
            var pref = ActorOf(Props.Create(() => new BehaviorOneActor(Name)));
            pref.Tell(new Cmd("a"));
            pref.Tell(GetState.Instance);
            ExpectMsg<object[]>().ShouldOnlyContainInOrder("a-1", "a-2");
        }

        [Fact]
        public void PersistentActor_throws_ActorKilledException_if_recovery_from_persisted_events_fails()
        {
            var supervisor = ActorOf(() => new Supervisor(TestActor));
            supervisor.Tell(Props.Create(() => new BehaviorOneActor(Name)));

            ExpectMsg<ActorRef>();
            ExpectMsg<ActorKilledException>();
        }
    }
}