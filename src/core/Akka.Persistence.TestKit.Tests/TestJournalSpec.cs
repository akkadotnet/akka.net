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
        public async Task works_as_memory_journal_by_default()
        {
            var actor = Sys.ActorOf<PersistActor>();

            // should pass
            await Journal.OnWrite.Pass();
            actor.Tell("write", TestActor);
            
            ExpectMsg("ack", TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task when_fail_on_write_is_set_all_writes_to_journal_will_fail()
        {
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            await Journal.OnWrite.Fail();
            actor.Tell("write", TestActor);
            
            ExpectTerminated(actor, TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task when_reject_on_write_is_set_all_writes_to_journal_will_be_rejected()
        {
            var actor = Sys.ActorOf<PersistActor>();
            Watch(actor);

            await Journal.OnWrite.Reject();
            actor.Tell("write", TestActor);

            ExpectMsg("rejected", TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task journal_must_reset_state_to_pass()
        {
            await WithJournalWrite(write => write.Fail(), () =>
            {
                var actor = Sys.ActorOf<PersistActor>();
                Watch(actor);

                actor.Tell("write", TestActor);
                ExpectTerminated(actor, TimeSpan.FromSeconds(3));
            });

            var actor2 = Sys.ActorOf<PersistActor>();
            Watch(actor2);

            actor2.Tell("write", TestActor);
            ExpectMsg("ack", TimeSpan.FromSeconds(3));
        }
    }
}