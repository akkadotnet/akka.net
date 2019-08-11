namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.Persistence.TestKit;
    using Xunit;

    public class TestJournalSpec : PersistenceTestKit
    {
        [Fact]
        public void must_return_ack_after_new_write_interceptor_is_set()
        {
            JournalActorRef.Tell(new TestJournal.UseWriteInterceptor(null), TestActor);

            ExpectMsg<TestJournal.Ack>(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void works_as_memory_journal_by_default()
        {
            var actor = Sys.ActorOf<PersistActor>();

            // should pass
            Journal.OnWrite.Pass();
            actor.Tell("write", TestActor);
            
            ExpectMsg("ack", TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void when_fail_on_write_is_set_all_writes_to_journal_will_fail()
        {
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            Journal.OnWrite.Fail();
            actor.Tell("write", TestActor);
            
            ExpectTerminated(actor, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public void when_reject_on_write_is_set_all_writes_to_journal_will_be_rejected()
        {
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            Journal.OnWrite.Reject();
            actor.Tell("write", TestActor);

            ExpectMsg("rejected", TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task during_recovery_by_setting_fail_will_cause_recovery_failure()
        {
            var actor = Sys.ActorOf<PersistActor>();
            await actor.Ask("write");
            await actor.GracefulStop(TimeSpan.FromSeconds(3));

            Journal.OnRecovery.Fail();
            actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            ExpectTerminated(actor, TimeSpan.FromSeconds(3));
        }
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