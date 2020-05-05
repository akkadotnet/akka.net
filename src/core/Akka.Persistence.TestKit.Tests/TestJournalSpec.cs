//-----------------------------------------------------------------------
// <copyright file="TestJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.TestKit.Tests
{
    using System;
    using System.Threading.Tasks;
    using Actor;
    using Akka.Persistence.TestKit;
    using Akka.TestKit;
    using Xunit;

    public class TestJournalSpec : PersistenceTestKit
    {
        public TestJournalSpec()
        {
            _probe = CreateTestProbe();
        }

        private readonly TestProbe _probe;

        [Fact]
        public void must_return_ack_after_new_write_interceptor_is_set()
        {
            JournalActorRef.Tell(new TestJournal.UseWriteInterceptor(null), TestActor);

            ExpectMsg<TestJournal.Ack>(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task works_as_memory_journal_by_default()
        {
            var actor = ActorOf(() => new PersistActor(_probe));

            await Journal.OnWrite.Pass();
            actor.Tell("write", TestActor);
            
            _probe.ExpectMsg("ack");
        }

        [Fact]
        public async Task when_fail_on_write_is_set_all_writes_to_journal_will_fail()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);

            await Journal.OnWrite.Fail();
            actor.Tell("write", TestActor);

            _probe.ExpectMsg("failure");
            ExpectTerminated(actor);
        }

        [Fact]
        public async Task when_reject_on_write_is_set_all_writes_to_journal_will_be_rejected()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);

            await Journal.OnWrite.Reject();
            actor.Tell("write", TestActor);

            _probe.ExpectMsg("rejected");
        }

        [Fact]
        public async Task journal_must_reset_state_to_pass()
        {
            await WithJournalWrite(write => write.Fail(), () =>
            {
                var actor = ActorOf(() => new PersistActor(_probe));
                Watch(actor);

                actor.Tell("write", TestActor);
                _probe.ExpectMsg("failure");
                ExpectTerminated(actor);
            });

            var actor2 = ActorOf(() => new PersistActor(_probe));
            Watch(actor2);

            actor2.Tell("write", TestActor);
            _probe.ExpectMsg("ack");
        }
    }
}
