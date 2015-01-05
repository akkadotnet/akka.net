using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.TestKit;
using Xunit;

namespace Akka.Persistence.Tests
{
    public class PersistentActorFailureSpec : PersistenceSpec
    {
        #region internal test classes

        internal class FailingMemoryJournal : AsyncWriteProxy
        {
            private readonly TimeSpan _timeout = TimeSpan.FromSeconds(5);

            protected override void PreStart()
            {
                base.PreStart();
                Self.Tell(new SetStore(Context.ActorOf(Props.Create<FailingMemoryStore>())));
            }
        }

        internal class FailingMemoryStore : MemoryStore
        {
            private bool FailingReceive(object message)
            {
                if (message is ReplayMessages)
                {
                    var replay = message as ReplayMessages;
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
                Sender.Tell(props != null ? Context.ActorOf(props) : message);
                return true;
            }
        }

        #endregion

        public PersistentActorFailureSpec()
            : base(PersistenceSpec.Configuration("inmem", "PersistentActorFailureSpec",
                extraConfig: @"akka.persistence.journal.inmem.class = ""Akka.Persistence.Tests.PersistentActorFailureSpec.FailingMemoryJournal"""))
        {
            
            var pref = ActorOf(Props.Create(() => new PersistentActorSpec.BehaviorOneActor(Name)));
            pref.Tell(new PersistentActorSpec.Cmd("a"));
            pref.Tell(GetState.Instance);
            ExpectMsg<IEnumerable<string>>().ShouldOnlyContainInOrder("a-1", "a-2");
        }

        [Fact]
        public void PersistentActor_throws_ActorKilledException_if_recovery_from_persisted_events_fails()
        {
            var supervisor = ActorOf(() => new Supervisor(TestActor));
            supervisor.Tell(Props.Create(() => new PersistentActorSpec.BehaviorOneActor(Name)));
            ExpectMsg<ActorRef>();
            ExpectMsg<ActorKilledException>();
        }
    }
}