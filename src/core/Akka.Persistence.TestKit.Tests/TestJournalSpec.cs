namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using Actor;
    using Akka.Persistence.TestKit;
    using Akka.TestKit;
    using Configuration;
    using Xunit;
    using Xunit.Abstractions;

    public class TestJournalSpec : AkkaSpec
    {
        public TestJournalSpec(ITestOutputHelper output = null)
            : base(GetConfig().ToString(), output)
        {
        }

        [Fact]
        public void must_return_ack_after_new_write_interceptor_is_set()
        {
            var journalActor = GetJournalRef(Sys);
            
            journalActor.Tell(new TestJournal.UseWriteInterceptor(null), TestActor);

            ExpectMsg<TestJournal.Ack>(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void works_as_memory_journal_by_default()
        {
            var journal = TestJournal.FromRef(GetJournalRef(Sys));
            
            var actor = Sys.ActorOf<PersistActor>();

            // should pass
            journal.OnWrite.Pass();
            actor.Tell("write", TestActor);
            ExpectMsg("ack", TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void when_fail_on_write_is_set_all_writes_to_journal_will_fail()
        {
            var journal = TestJournal.FromRef(GetJournalRef(Sys));
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            journal.OnWrite.Fail();
            actor.Tell("write", TestActor);
            
            ExpectTerminated(actor, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void when_reject_on_write_is_set_all_writes_to_journal_will_be_rejected()
        {
            var journal = TestJournal.FromRef(GetJournalRef(Sys));
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            journal.OnWrite.Reject();
            actor.Tell("write", TestActor);

            ExpectMsg("rejected", TimeSpan.FromSeconds(3));
        }

        static IActorRef GetJournalRef(ActorSystem sys) => Persistence.Instance.Apply(sys).JournalFor(null);

        static Config GetConfig()
            => ConfigurationFactory.FromResource<TestJournalSpec>("Akka.Persistence.TestKit.Tests.test-journal.conf");
    }

    public class PersistActor : UntypedPersistentActor
    {
        public override string PersistenceId  => "foo";

        protected override void OnCommand(object message)
        {
            var sender = Sender;
            switch (message as string)
            {
                case "write":
                    Persist(message, _ =>
                    {
                        sender.Tell("ack");
                    });
                    
                    break;
                
                default:
                    return;
            }
        }

        protected override void OnRecover(object message)
        {
            // noop
        }

        protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
        {
            Sender.Tell("rejected");

            base.OnPersistRejected(cause, @event, sequenceNr);
        }
    }
}