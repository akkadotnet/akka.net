//-----------------------------------------------------------------------
// <copyright file="JournalBehaviorSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Journal;
using Akka.Persistence.Journal;
using Akka.TestKit;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Journal
{
    /// <summary>Holds the writer thread at a known point so tests can queue work behind it without sleeping.</summary>
    internal sealed class WriterGate : IDisposable
    {
        private readonly ManualResetEventSlim _open = new(false);
        private readonly SemaphoreSlim _entered = new(0);
        private readonly ConcurrentQueue<(int Requests, int Rows)> _commits = new();

        public WriterGate(JournalWriter writer)
        {
            Writer = writer;
            writer.BeforeBatchForTests = () =>
            {
                _entered.Release();
                _open.Wait(TimeSpan.FromSeconds(30));
            };
            writer.AfterCommitForTests = (requests, rows) => _commits.Enqueue((requests, rows));
        }

        public JournalWriter Writer { get; }

        public IReadOnlyList<(int Requests, int Rows)> Commits => _commits.ToArray();

        /// <summary>Completes when the writer thread took an item and is held.</summary>
        public Task WaitUntilHeldAsync() => _entered.WaitAsync(TimeSpan.FromSeconds(10));

        /// <summary>Waits until the writer queue holds <paramref name="count"/> items, so nothing is still on its way to it.</summary>
        public void WaitUntilQueued(int count)
        {
            if (!SpinWait.SpinUntil(() => Writer.QueuedForTests >= count, TimeSpan.FromSeconds(10)))
                throw new TimeoutException($"The writer queue never held {count} items.");
        }

        public void Release() => _open.Set();

        public void Dispose()
        {
            _open.Set();
            Writer.BeforeBatchForTests = null;
        }
    }

    /// <summary>Persists strings as <see cref="TestEvent"/>, answers with its last sequence number and deletes on request.</summary>
    public sealed class TombstoneProbeActor : ReceivePersistentActor
    {
        public sealed record DeleteAll;

        public sealed record GetLastSequenceNr;

        private IActorRef? _deleteRequester;

        public TombstoneProbeActor(string persistenceId)
        {
            PersistenceId = persistenceId;

            Recover<TestEvent>(_ => { });
            Command<string>(value =>
            {
                var sender = Sender;
                Persist(new TestEvent(value), _ => sender.Tell(LastSequenceNr));
            });
            Command<GetLastSequenceNr>(_ => Sender.Tell(LastSequenceNr));
            Command<DeleteAll>(_ =>
            {
                _deleteRequester = Sender;
                DeleteMessages(LastSequenceNr);
            });
            Command<DeleteMessagesSuccess>(success => _deleteRequester?.Tell(success));
            Command<DeleteMessagesFailure>(failure => _deleteRequester?.Tell(new Status.Failure(failure.Cause)));
        }

        public override string PersistenceId { get; }
    }

    public abstract class JournalBehaviorSpecBase : EmbeddedSpec
    {
        private JournalWriter? _writer;

        protected JournalBehaviorSpecBase(SqliteTestDb db, Akka.Configuration.Config config, string name, ITestOutputHelper output)
            : base(db, config, name, output)
        {
        }

        /// <summary>Starts the journal and captures its writer. Call before anything else touches the journal.</summary>
        internal async Task CaptureWriterAsync()
        {
            // asked of the journal actor itself, so nothing depends on how the database path is spelled
            await InitializeJournalAsync();
            _writer = (await Journal.Ask<WriterForTests>(GetWriterForTests.Instance, Timeout)).Writer;
        }

        internal JournalWriter Writer => _writer!;

        /// <summary>Holds the writer at the next item it takes.</summary>
        internal WriterGate ArmGate() => new(_writer!);

        internal async Task<WriterGate> StartGatedAsync()
        {
            await CaptureWriterAsync();
            return ArmGate();
        }
    }

    public class JournalBehaviorSpec : JournalBehaviorSpecBase
    {
        public JournalBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private JournalBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, """
                    akka.persistence.journal.embedded {
                        buffer-size = 2
                        batch-size = 2
                    }
                    """),
                nameof(JournalBehaviorSpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_reject_only_failing_AtomicWrite_When_payload_cannot_be_serialized")]
        public async Task Should_reject_only_failing_AtomicWrite_When_payload_cannot_be_serialized()
        {
            var outcome = await WriteAsync(
                Write(Evt("reject", 1, new TestEvent("fine"))),
                Write(Evt("reject", 2, new UnserializableEvent("bad"))));

            outcome.Succeeded.Should().BeTrue();
            outcome.Written.Select(p => p.SequenceNr).Should().Equal(1L);
            outcome.Rejected.Should().HaveCount(1);
            outcome.Rejected[0].Persistent.SequenceNr.Should().Be(2L);

            var replay = await ReplayAsync("reject");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(1L);
        }

        [Fact(DisplayName = "Should_fail_write_with_buffer_full_message_When_queue_is_full")]
        public async Task Should_fail_write_with_buffer_full_message_When_queue_is_full()
        {
            using var gate = await StartGatedAsync();

            var first = CreateTestProbe();
            SendWrite(first, Write(Evt("full-1", 1, new TestEvent("a"))));
            await gate.WaitUntilHeldAsync();

            // buffer-size = 2: two more fit behind the held one, the fourth does not
            var second = CreateTestProbe();
            var third = CreateTestProbe();
            var fourth = CreateTestProbe();
            SendWrite(second, Write(Evt("full-2", 1, new TestEvent("b"))));
            SendWrite(third, Write(Evt("full-3", 1, new TestEvent("c"))));
            gate.WaitUntilQueued(2);
            SendWrite(fourth, Write(Evt("full-4", 1, new TestEvent("d"))));

            SpinWait.SpinUntil(() => gate.Writer.FullRejectionsForTests == 1, Timeout).Should().BeTrue("the fourth write must find the queue full");

            // replies leave the journal in write order, so the failure shows up once the held writes finished
            gate.Release();
            (await CollectWriteAsync(first, [Write(Evt("full-1", 1, new TestEvent("a")))])).Succeeded.Should().BeTrue();
            (await CollectWriteAsync(second, [Write(Evt("full-2", 1, new TestEvent("b")))])).Succeeded.Should().BeTrue();
            (await CollectWriteAsync(third, [Write(Evt("full-3", 1, new TestEvent("c")))])).Succeeded.Should().BeTrue();

            var failed = await fourth.ExpectMsgAsync<WriteMessagesFailed>(Timeout);
            failed.Cause.Message.Should().Be("Failed to enqueue journal row batch write, the queue buffer was full (2 elements)");
        }

        [Fact(DisplayName = "Should_group_requests_into_one_transaction_up_to_batch_size")]
        public async Task Should_group_requests_into_one_transaction_up_to_batch_size()
        {
            using var gate = await StartGatedAsync();

            var probes = Enumerable.Range(1, 3).Select(_ => CreateTestProbe()).ToArray();
            var writes = Enumerable.Range(1, 3).Select(i => Write(Evt($"group-{i}", 1, new TestEvent("x")))).ToArray();
            SendWrite(probes[0], writes[0]);
            await gate.WaitUntilHeldAsync();

            // buffer-size = 2 leaves room for two more. batch-size = 2: the held request takes the next one
            // into its transaction, the third waits for the next one.
            SendWrite(probes[1], writes[1]);
            SendWrite(probes[2], writes[2]);
            gate.WaitUntilQueued(2);
            gate.Release();

            for (var i = 0; i < 3; i++)
                (await CollectWriteAsync(probes[i], [writes[i]])).Succeeded.Should().BeTrue();

            gate.Commits.Should().Equal((2, 2), (1, 1));
        }

        [Fact(DisplayName = "Should_fail_whole_batch_When_unique_constraint_is_violated")]
        public async Task Should_fail_whole_batch_When_unique_constraint_is_violated()
        {
            await CaptureWriterAsync();
            (await WriteAsync(Write(Evt("dup", 1, new TestEvent("a"))))).Succeeded.Should().BeTrue();

            using var gate = ArmGate();
            var violating = CreateTestProbe();
            var innocent = CreateTestProbe();
            SendWrite(violating, Write(Evt("dup", 1, new TestEvent("again"))));
            await gate.WaitUntilHeldAsync();
            SendWrite(innocent, Write(Evt("innocent", 1, new TestEvent("c"))));
            gate.WaitUntilQueued(1);
            gate.Release();

            (await violating.ExpectMsgAsync<WriteMessagesFailed>(Timeout)).Cause.Should().BeOfType<SqliteException>();
            await innocent.ExpectMsgAsync<WriteMessagesFailed>(Timeout);

            (await ReplayAsync("innocent")).Replayed.Should().BeEmpty("the whole batch rolled back");
            (await WriteAsync(Write(Evt("innocent", 1, new TestEvent("c"))))).Succeeded.Should().BeTrue("the writer keeps running");
        }

        [Fact(DisplayName = "Should_wait_for_pending_write_When_reading_highest_sequence_number")]
        public async Task Should_wait_for_pending_write_When_reading_highest_sequence_number()
        {
            using var gate = await StartGatedAsync();

            var writeProbe = CreateTestProbe();
            SendWrite(writeProbe, Write(Evt("pending", 1, new TestEvent("a")), Evt("pending", 2, new TestEvent("b"))));
            await gate.WaitUntilHeldAsync();

            var replayProbe = CreateTestProbe();
            Journal.Tell(new ReplayMessages(0, long.MaxValue, long.MaxValue, "pending", replayProbe.Ref));
            await replayProbe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(300));

            gate.Release();
            await replayProbe.ExpectMsgAsync<ReplayedMessage>(Timeout);
            await replayProbe.ExpectMsgAsync<ReplayedMessage>(Timeout);
            (await replayProbe.ExpectMsgAsync<RecoverySuccess>(Timeout)).HighestSequenceNr.Should().Be(2L);
        }

        [Fact(DisplayName = "Should_fail_queued_writes_When_journal_stops")]
        public async Task Should_fail_queued_writes_When_journal_stops()
        {
            using var gate = await StartGatedAsync();

            var running = CreateTestProbe();
            var queued = CreateTestProbe();
            SendWrite(running, Write(Evt("stop-1", 1, new TestEvent("a"))));
            await gate.WaitUntilHeldAsync();

            // a delete is never batched with writes, so it stays queued behind the held write
            Journal.Tell(new DeleteMessagesTo("stop-2", 5, queued.Ref));
            gate.WaitUntilQueued(1);

            var stopping = Task.Run(() => gate.Writer.Stop());
            SpinWait.SpinUntil(() => gate.Writer.ShutdownRequested, Timeout).Should().BeTrue();
            gate.Release();
            await stopping;

            (await CollectWriteAsync(running, [Write(Evt("stop-1", 1, new TestEvent("a")))])).Succeeded.Should().BeTrue("a started transaction finishes");
            var failure = await queued.ExpectMsgAsync<DeleteMessagesFailure>(Timeout);
            failure.Cause.Message.Should().Be("SQLite journal stopped before this write ran.");

            var late = CreateTestProbe();
            SendWrite(late, Write(Evt("stop-3", 1, new TestEvent("c"))));
            (await late.ExpectMsgAsync<WriteMessagesFailed>(Timeout)).Cause.Message
                .Should().Be("Failed to enqueue journal row batch write, the queue was closed.");
        }

        [Fact(DisplayName = "Should_keep_tombstone_and_its_tags_When_deleting_messages")]
        public async Task Should_keep_tombstone_and_its_tags_When_deleting_messages()
        {
            await WriteAsync(Write(
                Evt("del", 1, new Tagged(new TestEvent("a"), new[] { "t1" })),
                Evt("del", 2, new Tagged(new TestEvent("b"), new[] { "t2" })),
                Evt("del", 3, new Tagged(new TestEvent("c"), new[] { "t3" }))));

            await DeleteToAsync("del", 2);

            Db.Query("SELECT sequence_number, deleted FROM journal WHERE persistence_id = 'del' ORDER BY sequence_number")
                .Select(r => (r[0], r[1])).Should().Equal((2L, 1L), (3L, 0L));
            Db.Query("SELECT tag FROM tags WHERE persistence_id = 'del' ORDER BY tag")
                .Select(r => r[0]).Should().Equal("t2", "t3");

            var replay = await ReplayAsync("del");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(3L);
            replay.HighestSequenceNr.Should().Be(3L);
        }

        [Fact(DisplayName = "Should_blank_the_tombstone_message_When_deleting_messages")]
        public async Task Should_blank_the_tombstone_message_When_deleting_messages()
        {
            await WriteAsync(Write(Evt("blank", 1, new TestEvent("a")), Evt("blank", 2, new TestEvent("b")), Evt("blank", 3, new TestEvent("c"))));

            await DeleteToAsync("blank", 2);

            var rows = Db.Query("SELECT sequence_number, deleted, typeof(message), length(message), manifest, identifier FROM journal WHERE persistence_id = 'blank' ORDER BY sequence_number");
            rows.Should().HaveCount(2);
            // the tombstone: still there, deleted, an empty blob (the column is NOT NULL), manifest and identifier untouched
            rows[0].Should().Equal(2L, 1L, "blob", 0L, "E", 7301L);
            // the row above it is untouched
            rows[1][0].Should().Be(3L);
            rows[1][1].Should().Be(0L);
            ((long)rows[1][3]!).Should().BeGreaterThan(0L);
        }

        [Fact(DisplayName = "Should_continue_from_the_next_sequence_number_When_all_events_of_a_persistence_id_are_deleted")]
        public async Task Should_continue_from_the_next_sequence_number_When_all_events_of_a_persistence_id_are_deleted()
        {
            var actor = Sys.ActorOf(Props.Create(() => new TombstoneProbeActor("tombstone")), "tombstone-1");
            foreach (var expected in new[] { 1L, 2L, 3L })
                (await actor.Ask<long>("event", Timeout)).Should().Be(expected);

            await actor.Ask<DeleteMessagesSuccess>(new TombstoneProbeActor.DeleteAll(), Timeout);

            // only the tombstone is left: highest sequence number 3, deleted, no payload
            Db.Query("SELECT sequence_number, deleted, typeof(message), length(message) FROM journal WHERE persistence_id = 'tombstone'")
                .Should().ContainSingle().Which.Should().Equal(3L, 1L, "blob", 0L);
            (await ReplayAsync("tombstone")).Replayed.Should().BeEmpty();

            Watch(actor);
            Sys.Stop(actor);
            await ExpectTerminatedAsync(actor, Timeout);

            // the new incarnation recovers sequence number 3 from the tombstone and goes on with 4
            var reborn = Sys.ActorOf(Props.Create(() => new TombstoneProbeActor("tombstone")), "tombstone-2");
            (await reborn.Ask<long>(new TombstoneProbeActor.GetLastSequenceNr(), Timeout)).Should().Be(3L);
            (await reborn.Ask<long>("after", Timeout)).Should().Be(4L);

            Db.Query("SELECT sequence_number, deleted, length(message) > 0 FROM journal WHERE persistence_id = 'tombstone' ORDER BY sequence_number")
                .Select(r => (r[0], r[1], r[2])).Should().Equal((3L, 1L, 0L), (4L, 0L, 1L));
            var replay = await ReplayAsync("tombstone");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(4L);
            replay.HighestSequenceNr.Should().Be(4L);
        }

        [Fact(DisplayName = "Should_report_highest_sequence_number_When_all_events_are_deleted")]
        public async Task Should_report_highest_sequence_number_When_all_events_are_deleted()
        {
            await WriteAsync(Write(Evt("all-gone", 1, new TestEvent("a")), Evt("all-gone", 2, new TestEvent("b"))));

            await DeleteToAsync("all-gone", 100);

            var replay = await ReplayAsync("all-gone");
            replay.Replayed.Should().BeEmpty();
            replay.HighestSequenceNr.Should().Be(2L);
        }

        [Fact(DisplayName = "Should_read_highest_sequence_number_above_from_When_recovering_from_a_snapshot")]
        public async Task Should_read_highest_sequence_number_above_from_When_recovering_from_a_snapshot()
        {
            await WriteAsync(Write(Evt("from-snapshot", 1, new TestEvent("a")), Evt("from-snapshot", 2, new TestEvent("b")), Evt("from-snapshot", 3, new TestEvent("c"))));

            // a recovery from a snapshot at sequence number 2 starts replaying at 3
            var after = await ReplayAsync("from-snapshot", from: 3);
            after.Replayed.Select(p => p.SequenceNr).Should().Equal(3L);
            after.HighestSequenceNr.Should().Be(3L);

            // nothing above the snapshot: the journal reports 0 and core keeps the snapshot's sequence number
            var beyond = await ReplayAsync("from-snapshot", from: 4);
            beyond.Replayed.Should().BeEmpty();
            beyond.HighestSequenceNr.Should().Be(0L);
        }

        [Fact(DisplayName = "Should_do_nothing_When_delete_target_is_below_first_event")]
        public async Task Should_do_nothing_When_delete_target_is_below_first_event()
        {
            await WriteAsync(Write(Evt("below", 3, new TestEvent("a")), Evt("below", 4, new TestEvent("b"))));

            await DeleteToAsync("below", 2);

            Db.Query("SELECT sequence_number, deleted FROM journal WHERE persistence_id = 'below' ORDER BY sequence_number")
                .Select(r => (r[0], r[1])).Should().Equal((3L, 0L), (4L, 0L));
        }

        [Fact(DisplayName = "Should_fail_replay_with_clear_message_When_identifier_is_null")]
        public async Task Should_fail_replay_with_clear_message_When_identifier_is_null()
        {
            await WriteAsync(Write(Evt("legacy", 1, new TestEvent("a"))));
            Db.Execute("UPDATE journal SET identifier = NULL WHERE persistence_id = 'legacy'");

            var replay = await ReplayAsync("legacy");

            replay.Failure.Should().NotBeNull();
            replay.Failure!.Message.Should().Contain("Journal row (legacy, 1) has a NULL identifier");
        }

        [Fact(DisplayName = "Should_skip_request_When_token_is_cancelled_before_batch_starts")]
        public async Task Should_skip_request_When_token_is_cancelled_before_batch_starts()
        {
            using var gate = await StartGatedAsync();
            var plain = CreateTestProbe();
            SendWrite(plain, Write(Evt("cancel-1", 1, new TestEvent("a"))));
            await gate.WaitUntilHeldAsync();

            using var cts = new CancellationTokenSource();
            var row = new JournalRow
            {
                Created = 1,
                PersistenceId = "cancel-2",
                SequenceNr = 1,
                Message = [1],
                Manifest = "",
                Identifier = 7301,
                Tags = [],
                WriterUuid = "w"
            };
            var request = new WriteRequest([row], cts.Token);
            gate.Writer.TryEnqueue(request).Should().Be(EnqueueResult.Queued);
            cts.Cancel();
            gate.Release();

            (await CollectWriteAsync(plain, [Write(Evt("cancel-1", 1, new TestEvent("a")))])).Succeeded.Should().BeTrue();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.Completion.Task);
            (await ReplayAsync("cancel-2")).Replayed.Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_open_a_fresh_connection_When_the_writer_resets_after_a_sqlite_error")]
        public async Task Should_open_a_fresh_connection_When_the_writer_resets_after_a_sqlite_error()
        {
            await CaptureWriterAsync();
            (await WriteAsync(Write(Evt("reset", 1, new TestEvent("a"))))).Succeeded.Should().BeTrue();
            Writer.HolderConnectionStringForTests.Should().Contain("Pooling=False", "a reset must really close the handle");
            Writer.ConnectionResetsForTests.Should().Be(0);

            // "no such table" is SQLITE_ERROR, which is not busy, locked or a constraint violation: the writer drops the connection
            Db.Execute("ALTER TABLE journal RENAME TO journal_moved");
            var failed = await WriteAsync(Write(Evt("reset", 2, new TestEvent("b"))));
            failed.Succeeded.Should().BeFalse();
            failed.Failure.Should().BeOfType<SqliteException>();
            Writer.ConnectionResetsForTests.Should().Be(1);

            Db.Execute("ALTER TABLE journal_moved RENAME TO journal");
            (await WriteAsync(Write(Evt("reset", 2, new TestEvent("b"))))).Succeeded.Should().BeTrue("the next write opens a new connection");
            Writer.ConnectionResetsForTests.Should().Be(1);
            (await ReplayAsync("reset")).Replayed.Select(p => p.SequenceNr).Should().Equal(1L, 2L);
        }

        [Fact(DisplayName = "Should_wait_for_lock_When_another_connection_holds_write_transaction")]
        public async Task Should_wait_for_lock_When_another_connection_holds_write_transaction()
        {
            await InitializeJournalAsync();

            using var other = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Db.FilePath, Pooling = false }.ConnectionString);
            other.Open();
            using (var begin = other.CreateCommand())
            {
                begin.CommandText = "BEGIN IMMEDIATE";
                begin.ExecuteNonQuery();
            }

            var probe = CreateTestProbe();
            var writes = new[] { Write(Evt("locked", 1, new TestEvent("a"))) };
            SendWrite(probe, writes);
            await probe.ExpectNoMsgAsync(TimeSpan.FromMilliseconds(500));

            using (var end = other.CreateCommand())
            {
                end.CommandText = "ROLLBACK";
                end.ExecuteNonQuery();
            }

            (await CollectWriteAsync(probe, writes)).Succeeded.Should().BeTrue();
        }
    }
}
