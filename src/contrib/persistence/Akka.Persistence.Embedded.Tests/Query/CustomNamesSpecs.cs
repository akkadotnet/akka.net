//-----------------------------------------------------------------------
// <copyright file="CustomNamesSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Query
{
    /// <summary>
    /// Every table has a non-default name. The columns keep their fixed names, so this shows a table name that a statement
    /// leaves out or hard codes.
    /// </summary>
    internal static class CustomNames
    {
        public const string Hocon = """
            akka.persistence.journal.embedded {
                table-name = evt
                tag-table-name = tg
            }
            akka.persistence.snapshot-store.embedded {
                table-name = snp
            }
            """;
    }

    /// <summary>Capture workload, deletes, tags and snapshots, all with custom table names.</summary>
    public class CustomNamesQuerySpec : CaptureWorkloadQuerySpecBase
    {
        public CustomNamesQuerySpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private CustomNamesQuerySpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, CustomNames.Hocon), nameof(CustomNamesQuerySpec), output)
        {
        }

        [Fact(DisplayName = "Should_write_replay_delete_and_snapshot_through_custom_names_When_every_table_name_is_custom")]
        public async Task Should_write_replay_delete_and_snapshot_through_custom_names_When_every_table_name_is_custom()
        {
            await WriteOneAsync("names", 1, new TestEvent("a"), "red");
            await WriteOneAsync("names", 2, new TestEvent("b"), "red", "blue");
            await WriteOneAsync("names", 3, new TestEvent("c"));

            // the rows are where the custom names say
            Db.Query("SELECT persistence_id, sequence_number FROM evt WHERE persistence_id = 'names' ORDER BY sequence_number")
                .Select(r => ((string)r[0]!, (long)r[1]!)).Should().Equal(("names", 1L), ("names", 2L), ("names", 3L));
            Db.Query("SELECT tag, persistence_id, sequence_nr FROM tg ORDER BY ordering_id, tag")
                .Select(r => ((string)r[0]!, (string)r[1]!, (long)r[2]!))
                .Should().Equal(("red", "names", 1L), ("blue", "names", 2L), ("red", "names", 2L));
            Db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('journal', 'tags', 'snapshot')")
                .Should().BeEmpty("no table with a default name exists");

            var replayed = await ReplayAsync("names");
            replayed.Replayed.Select(r => r.SequenceNr).Should().Equal(1L, 2L, 3L);
            replayed.HighestSequenceNr.Should().Be(3L);

            // the tombstone keeps the highest sequence number after the other rows are gone
            await DeleteToAsync("names", 3);
            Db.Query("SELECT sequence_number, deleted FROM evt WHERE persistence_id = 'names'").Select(r => (r[0], r[1])).Should().Equal((3L, 1L));
            Db.Query("SELECT count(*) FROM tg WHERE persistence_id = 'names'").Single()[0].Should().Be(0L);
            var afterDelete = await ReplayAsync("names");
            afterDelete.Replayed.Should().BeEmpty();
            afterDelete.HighestSequenceNr.Should().Be(3L);

            var byTag = await RunAsync(ReadJournal.CurrentEventsByTag("red", Akka.Persistence.Query.NoOffset.Instance));
            byTag.Should().BeEmpty("the red events were deleted");

            // snapshot store
            var probe = CreateTestProbe();
            var created = new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc);
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("names", 3, created), new TestEvent("snap")), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
            SnapshotStore.Tell(new LoadSnapshot("names", SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            var loaded = await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout);
            loaded.Snapshot!.Metadata.SequenceNr.Should().Be(3L);
            loaded.Snapshot.Snapshot.Should().Be(new TestEvent("snap"));
            Db.Query("SELECT persistence_id, sequence_number, created FROM snp").Select(r => ((string)r[0]!, (long)r[1]!, (long)r[2]!))
                .Should().Equal(("names", 3L, created.Ticks));
        }
    }

    /// <summary>Recovery in several pages: <c>replay-batch-size</c> is 3, so ten events take four round trips.</summary>
    public class ReplayPagingSpec : EmbeddedSpec
    {
        public ReplayPagingSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private ReplayPagingSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, "akka.persistence.journal.embedded.replay-batch-size = 3"),
                nameof(ReplayPagingSpec),
                output)
        {
        }

        private async Task WriteTenAsync(string pid)
        {
            var events = Enumerable.Range(1, 10).Select(i => Evt(pid, i, new TestEvent($"e-{i}"))).ToArray();
            (await WriteAsync(Write(events))).Succeeded.Should().BeTrue();
        }

        [Fact(DisplayName = "Should_replay_every_event_in_order_When_the_range_spans_several_pages")]
        public async Task Should_replay_every_event_in_order_When_the_range_spans_several_pages()
        {
            await WriteTenAsync("paged-all");

            var outcome = await ReplayAsync("paged-all");

            outcome.Failure.Should().BeNull();
            outcome.Replayed.Select(r => r.SequenceNr).Should().Equal(Enumerable.Range(1, 10).Select(i => (long)i));
            outcome.Replayed.Select(r => ((TestEvent)r.Payload).Value).Should().Equal(Enumerable.Range(1, 10).Select(i => $"e-{i}"));
            outcome.HighestSequenceNr.Should().Be(10L);
        }

        [Fact(DisplayName = "Should_stop_at_max_and_respect_bounds_When_the_range_spans_several_pages")]
        public async Task Should_stop_at_max_and_respect_bounds_When_the_range_spans_several_pages()
        {
            await WriteTenAsync("paged-max");

            var limited = await ReplayAsync("paged-max", max: 7);
            limited.Replayed.Select(r => r.SequenceNr).Should().Equal(Enumerable.Range(1, 7).Select(i => (long)i));
            limited.HighestSequenceNr.Should().Be(10L, "the highest sequence number ignores the limit");

            var window = await ReplayAsync("paged-max", from: 4, to: 9);
            window.Replayed.Select(r => r.SequenceNr).Should().Equal(4L, 5L, 6L, 7L, 8L, 9L);

            var exactPage = await ReplayAsync("paged-max", from: 4, to: 6);
            exactPage.Replayed.Select(r => r.SequenceNr).Should().Equal(4L, 5L, 6L);
        }

        [Fact(DisplayName = "Should_skip_deleted_events_across_pages_When_the_prefix_was_deleted")]
        public async Task Should_skip_deleted_events_across_pages_When_the_prefix_was_deleted()
        {
            await WriteTenAsync("paged-deleted");
            await DeleteToAsync("paged-deleted", 5);

            var outcome = await ReplayAsync("paged-deleted");

            outcome.Replayed.Select(r => r.SequenceNr).Should().Equal(6L, 7L, 8L, 9L, 10L);
            outcome.HighestSequenceNr.Should().Be(10L);
        }
    }
}
