using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting.TestKit.Tests.TestActorRefTests;
using Akka.Persistence;
using Akka.Persistence.TestKit;
using Akka.TestKit;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestPersistenceTestKistTests;

public class TestJournalSpec : PersistenceTestKit
{
    private TestProbe _probe = null!; 

    public TestJournalSpec(ITestOutputHelper output) : base(nameof(TestJournalSpec), output)
    {
    }

    // Expect should be passing by default, need to make them less sensitive to timing
    protected override Config? Config => "akka.test.single-expect-default = 30s";

    protected override Task BeforeTestStart()
    {
        _probe = CreateTestProbe();
        return Task.CompletedTask;
    }

    [Fact]
    public void must_have_journal_and_snapshot()
    {
        Journal.Should().NotBeNull();
        JournalActorRef.Should().NotBeNull();
        Snapshots.Should().NotBeNull();
        SnapshotsActorRef.Should().NotBeNull();
    }

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
        await WatchAsync(actor);
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
        await WatchAsync(actor);
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
        await WatchAsync(actor);
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
        await WatchAsync(actor);
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
            await WatchAsync(actor);
            await _probe.ExpectMsgAsync<RecoveryCompleted>();

            actor.Tell(new PersistActor.WriteMessage("write"), TestActor);
            await _probe.ExpectMsgAsync("failure");
            await ExpectTerminatedAsync(actor);
        });

        var actor2 = ActorOf(() => new PersistActor(_probe));
        await WatchAsync(actor2);

        await _probe.ExpectMsgAsync<RecoveryCompleted>();
        actor2.Tell(new PersistActor.WriteMessage("write"), TestActor);
        await _probe.ExpectMsgAsync("ack");
    }
}