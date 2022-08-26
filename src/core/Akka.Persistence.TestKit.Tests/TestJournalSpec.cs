//-----------------------------------------------------------------------
// <copyright file="TestJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------


using Akka.Configuration;

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
        // Expect should be passing by default, need to make them less sencitive to timing
        private static readonly Config DefaultTimeoutConfig = "akka.test.single-expect-default = 30s";
        
        public TestJournalSpec() : base(DefaultTimeoutConfig)
        {
            _probe = CreateTestProbe();
        }

        private readonly TestProbe _probe;

        [Fact]
        public async Task must_return_ack_after_new_write_interceptor_is_set()
        {
            JournalActorRef.Tell(new TestJournal.UseWriteInterceptor(null), TestActor);

            await ExpectMsgAsync<TestJournal.Ack>(TimeSpan.FromSeconds(3));
        }

        [Fact]
        public async Task works_as_memory_journal_by_default()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            await Journal.OnWrite.Pass();
            actor.Tell(new PersistActor.WriteMessage("write"), TestActor);
            
            await _probe.ExpectMsgAsync("ack");
        }

        [Fact]
        public async Task must_recover_restarted_actor()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            await Journal.OnRecovery.Pass();
            actor.Tell(new PersistActor.WriteMessage("1"), TestActor);
            await _probe.ExpectMsgAsync("ack");
            actor.Tell(new PersistActor.WriteMessage("2"), TestActor);
            await _probe.ExpectMsgAsync("ack");

            await actor.GracefulStop(TimeSpan.FromSeconds(1));
            await ExpectTerminatedAsync(actor);

            ActorOf(() => new PersistActor(_probe));
            await _probe.ExpectMsgAsync("1");
            await _probe.ExpectMsgAsync("2");
            await _probe.ExpectMsgAsync<RecoveryCompleted>();
        }

        [Fact]
        public async Task when_fail_on_write_is_set_all_writes_to_journal_will_fail()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            await Journal.OnWrite.Fail();
            actor.Tell(new PersistActor.WriteMessage("write"), TestActor);

            await _probe.ExpectMsgAsync("failure");
            await ExpectTerminatedAsync(actor);
        }

        [Fact]
        public async Task must_recover_failed_actor()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            await Journal.OnRecovery.Pass();
            actor.Tell(new PersistActor.WriteMessage("1"), TestActor);
            await _probe.ExpectMsgAsync("ack");
            actor.Tell(new PersistActor.WriteMessage("2"), TestActor);
            await _probe.ExpectMsgAsync("ack");

            await Journal.OnWrite.Fail();
            actor.Tell(new PersistActor.WriteMessage("3"), TestActor);

            await _probe.ExpectMsgAsync("failure");
            await ExpectTerminatedAsync(actor);

            ActorOf(() => new PersistActor(_probe));
            await _probe.ExpectMsgAsync("1");
            await _probe.ExpectMsgAsync("2");
            await _probe.ExpectMsgAsync<RecoveryCompleted>();
        }

        [Fact]
        public async Task when_reject_on_write_is_set_all_writes_to_journal_will_be_rejected()
        {
            var actor = ActorOf(() => new PersistActor(_probe));
            Watch(actor);
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            await Journal.OnWrite.Reject();
            actor.Tell(new PersistActor.WriteMessage("write"), TestActor);

            await _probe.ExpectMsgAsync("rejected");
        }

        [Fact]
        public async Task journal_must_reset_state_to_pass()
        {
            await WithJournalWrite(write => write.Fail(), async () =>
            {
                var actor = ActorOf(() => new PersistActor(_probe));
                Watch(actor);
                await _probe.ExpectMsgAsync<RecoveryCompleted>();

                actor.Tell(new PersistActor.WriteMessage("write"), TestActor);
                await _probe.ExpectMsgAsync("failure");
                await ExpectTerminatedAsync(actor);
            });

            var actor2 = ActorOf(() => new PersistActor(_probe));
            Watch(actor2);

            await _probe.ExpectMsgAsync<RecoveryCompleted>();
            actor2.Tell(new PersistActor.WriteMessage("write"), TestActor);
            await _probe.ExpectMsgAsync("ack");
        }
    }
}
