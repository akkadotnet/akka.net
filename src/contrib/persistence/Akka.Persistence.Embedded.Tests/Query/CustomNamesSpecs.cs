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
    /// Every table and column has a non-default name. Several default names are shared (<c>persistence_id</c> in three
    /// tables, <c>manifest</c> in two, <c>sequence_number</c> in three), so a column name swapped in a statement hides
    /// behind the defaults and shows here.
    /// </summary>
    internal static class CustomNames
    {
        public const string Hocon = """
            akka.persistence.journal.embedded {
                default {
                    journal {
                        table-name = evt
                        columns {
                            ordering = o, created = created_at_ticks, deleted = is_deleted_flag, persistence-id = pid
                            sequence-number = seq, message = payload_blob, tags = tgs, manifest = man, identifier = ident
                            writer-uuid = writer_uuid_long_name_x
                        }
                    }
                    metadata {
                        table-name = md
                        columns { persistence-id = mpid, sequence-number = mseq }
                    }
                    tag {
                        table-name = tg
                        columns { ordering-id = oid, tag-value = tg_value_column, persistence-id = tpid, sequence-nr = tseq }
                    }
                }
            }
            akka.persistence.snapshot-store.embedded {
                default.snapshot {
                    table-name = snp
                    columns { persistence-id = spid, sequence-number = sseq, created = sc, snapshot = payload_snapshot_bytes, manifest = sman, serializerId = sid }
                }
            }
            """;
    }

    /// <summary>Capture workload, delete-compatibility, tag table and writer uuid, all with custom names.</summary>
    public class CustomNamesBothQuerySpec : CaptureWorkloadQuerySpecBase
    {
        public CustomNamesBothQuerySpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private CustomNamesBothQuerySpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, TagWriteMode.Both, deleteCompat: true, extra: CustomNames.Hocon),
                nameof(CustomNamesBothQuerySpec),
                output)
        {
        }

        [Fact(DisplayName = "Should_write_replay_delete_and_snapshot_through_custom_names_When_every_name_is_custom")]
        public async Task Should_write_replay_delete_and_snapshot_through_custom_names_When_every_name_is_custom()
        {
            await WriteOneAsync("names", 1, new TestEvent("a"), "red");
            await WriteOneAsync("names", 2, new TestEvent("b"), "red", "blue");
            await WriteOneAsync("names", 3, new TestEvent("c"));

            // the rows are where the custom names say
            Db.Query("SELECT pid, seq, ident, writer_uuid_long_name_x, tgs FROM evt WHERE pid = 'names' ORDER BY seq")
                .Select(r => ((string)r[0]!, (long)r[1]!)).Should().Equal(("names", 1L), ("names", 2L), ("names", 3L));
            Db.Query("SELECT tg_value_column, tpid, tseq FROM tg ORDER BY oid, tg_value_column")
                .Select(r => ((string)r[0]!, (string)r[1]!, (long)r[2]!))
                .Should().Equal(("red", "names", 1L), ("blue", "names", 2L), ("red", "names", 2L));
            Db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('journal', 'tags', 'snapshot', 'journal_metadata')")
                .Should().BeEmpty("no table with a default name exists");

            var replayed = await ReplayAsync("names");
            replayed.Replayed.Select(r => r.SequenceNr).Should().Equal(1L, 2L, 3L);
            replayed.HighestSequenceNr.Should().Be(3L);

            // delete-compatibility: the metadata row keeps the highest sequence number after the rows are gone
            await DeleteToAsync("names", 3);
            Db.Query("SELECT mpid, mseq FROM md WHERE mpid = 'names'").Select(r => ((string)r[0]!, (long)r[1]!)).Should().Equal(("names", 3L));
            var afterDelete = await ReplayAsync("names");
            afterDelete.Replayed.Should().BeEmpty();
            afterDelete.HighestSequenceNr.Should().Be(3L);

            var byTag = await RunAsync(ReadJournal.CurrentEventsByTag("red", Akka.Persistence.Query.NoOffset.Instance));
            byTag.Should().BeEmpty("the red events were deleted");

            // snapshot store
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("names", 3, new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc)), new TestEvent("snap")), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
            SnapshotStore.Tell(new LoadSnapshot("names", SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            var loaded = await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout);
            loaded.Snapshot!.Metadata.SequenceNr.Should().Be(3L);
            loaded.Snapshot.Snapshot.Should().Be(new TestEvent("snap"));
            Db.Query("SELECT spid, sseq, sc FROM snp").Select(r => ((string)r[0]!, (long)r[1]!, (long)r[2]!)).Should().Equal(("names", 3L, new System.DateTime(2026, 1, 1, 12, 0, 0, System.DateTimeKind.Utc).Ticks));
        }
    }

    /// <summary>Capture workload in Csv mode with custom names, so the <c>tags</c> column name is exercised.</summary>
    public class CustomNamesCsvQuerySpec : CaptureWorkloadQuerySpecBase
    {
        public CustomNamesCsvQuerySpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private CustomNamesCsvQuerySpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, TagWriteMode.Csv, extra: CustomNames.Hocon),
                nameof(CustomNamesCsvQuerySpec),
                output)
        {
        }

        protected override long LastRedOrdering => 6L;

        [Fact(DisplayName = "Should_store_tags_in_the_custom_tags_column_When_tag_mode_is_Csv")]
        public async Task Should_store_tags_in_the_custom_tags_column_When_tag_mode_is_Csv()
        {
            await WriteOneAsync("csv-names", 1, new TestEvent("a"), "red", "blue");

            var column = (string)Db.Query("SELECT tgs FROM evt WHERE pid = 'csv-names'")[0][0]!;
            column.Trim(';').Split(';').Should().BeEquivalentTo("red", "blue");
            Db.Query("SELECT name FROM sqlite_master WHERE type = 'table' AND name IN ('tg', 'tags')").Should().BeEmpty();

            var found = await RunAsync(ReadJournal.CurrentEventsByTag("blue", Akka.Persistence.Query.NoOffset.Instance));
            found.Select(e => e.PersistenceId).Should().Equal("csv-names");
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
                SqliteSpecConfig.Create(db, SqliteTestMode.TT, "akka.persistence.journal.embedded.replay-batch-size = 3"),
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
