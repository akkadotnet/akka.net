using System;
using System.Linq;
using Akka.Actor;
using Akka.Persistence.Journal;

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

        }
    }
}