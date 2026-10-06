//-----------------------------------------------------------------------
// <copyright file="ApsCompatSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Embedded.Tests.Query;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Serialization;
using Akka.Streams.Dsl;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Compat
{
    /// <summary>The record in the fixture. Its serializer is defined identically in the generator (TestData/aps-compat/Program.cs.txt).</summary>
    public sealed record CompatItem(string Name, int Qty);

    public sealed class CompatItemSerializer : SerializerWithStringManifest
    {
        public CompatItemSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 9001;

        public override string Manifest(object o) => "I";

        public override byte[] ToBinary(object obj)
        {
            var item = (CompatItem)obj;
            return Encoding.UTF8.GetBytes($"{item.Name};{item.Qty}");
        }

        public override object FromBinary(byte[] bytes, string manifest)
        {
            var parts = Encoding.UTF8.GetString(bytes).Split(';');
            return new CompatItem(parts[0], int.Parse(parts[1]));
        }
    }

    /// <summary>
    /// Runs Akka.Persistence.Embedded on a database that Akka.Persistence.Sql 1.5.70 wrote with its default SQLite settings
    /// (see TestData/aps-compat/README.md). Every test works on its own copy of the file.
    /// </summary>
    public class ApsCompatSpec : QueryBehaviorSpecBase
    {
        private static readonly string FixturePath =
            Path.Combine(AppContext.BaseDirectory, "TestData", "aps-compat", "aps-1.5.70.db");

        private static readonly string SerializerHocon = $$"""
            akka.actor {
                serializers.compat-item = "{{typeof(CompatItemSerializer).AssemblyQualifiedName}}"
                serialization-bindings."{{typeof(CompatItem).AssemblyQualifiedName}}" = compat-item
            }
            """;

        private const string SchemaSql = "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name";

        private readonly ITestOutputHelper _output;

        public ApsCompatSpec(ITestOutputHelper output) : this(CopyFixture(), output)
        {
        }

        private ApsCompatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SerializerHocon), nameof(ApsCompatSpec), output)
        {
            _output = output;
        }

        private static SqliteTestDb CopyFixture()
        {
            var db = new SqliteTestDb();
            File.Copy(FixturePath, db.FilePath);
            return db;
        }

        private static (long SequenceNr, object Payload)[] Events(ReplayOutcome outcome)
            => outcome.Replayed.Select(p => (p.SequenceNr, p.Payload)).ToArray();

        private static (long Offset, string PersistenceId, long SequenceNr, object Event, string Tags)[] Describe(IEnumerable<EventEnvelope> envelopes)
            => envelopes.Select(e => (((Sequence)e.Offset).Value, e.PersistenceId, e.SequenceNr, e.Event, Tags(e))).ToArray();

        private async Task<SelectedSnapshot?> LoadSnapshotAsync(string pid)
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            return (await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout)).Snapshot;
        }

        private async Task<string[]> PersistenceIdsAsync()
            => (await ReadJournal.CurrentPersistenceIds().RunWith(Sink.Seq<string>(), Mat).WaitAsync(Timeout))
                .OrderBy(i => i, StringComparer.Ordinal).ToArray();

        [Fact(DisplayName = "Should_leave_existing_tables_unchanged_When_started_on_aps_database")]
        public async Task Should_leave_existing_tables_unchanged_When_started_on_aps_database()
        {
            var schemaBefore = Db.Query(SchemaSql);
            var journalBefore = Db.Query("SELECT ordering, created, deleted, persistence_id, sequence_number, hex(message), manifest, identifier, writer_uuid FROM journal ORDER BY ordering");
            var tagsBefore = Db.Query("SELECT ordering_id, tag, sequence_nr, persistence_id FROM tags ORDER BY ordering_id, tag");
            var snapshotsBefore = Db.Query("SELECT persistence_id, sequence_number, created, hex(snapshot), manifest, serializer_id FROM snapshot");
            schemaBefore.Should().NotBeEmpty();

            await InitializeJournalAsync();
            await InitializeSnapshotStoreAsync();
            // read everything once so a lazy schema or data fix-up would show
            await ReplayAsync("alpha");
            await RunAsync(ReadJournal.CurrentAllEvents(NoOffset.Instance));
            await LoadSnapshotAsync("alpha");

            Db.Query(SchemaSql).Should().BeEquivalentTo(schemaBefore, o => o.WithStrictOrdering());
            Db.Query("SELECT ordering, created, deleted, persistence_id, sequence_number, hex(message), manifest, identifier, writer_uuid FROM journal ORDER BY ordering")
                .Should().BeEquivalentTo(journalBefore, o => o.WithStrictOrdering());
            Db.Query("SELECT ordering_id, tag, sequence_nr, persistence_id FROM tags ORDER BY ordering_id, tag")
                .Should().BeEquivalentTo(tagsBefore, o => o.WithStrictOrdering());
            Db.Query("SELECT persistence_id, sequence_number, created, hex(snapshot), manifest, serializer_id FROM snapshot")
                .Should().BeEquivalentTo(snapshotsBefore, o => o.WithStrictOrdering());
        }

        [Fact(DisplayName = "Should_replay_events_in_order_and_report_highest_sequence_number_When_database_was_written_by_aps")]
        public async Task Should_replay_events_in_order_and_report_highest_sequence_number_When_database_was_written_by_aps()
        {
            // alpha: events 1-3 were deleted (the tombstone is row 3)
            var alpha = await ReplayAsync("alpha");
            alpha.Failure.Should().BeNull();
            Events(alpha).Should().Equal(
                (4L, "alpha-4"), (5L, "alpha-5"), (6L, "alpha-6"), (7L, "alpha-7"), (8L, "alpha-8"));
            alpha.HighestSequenceNr.Should().Be(8);

            var beta = await ReplayAsync("beta");
            Events(beta).Should().Equal(
                (1L, "beta-1"), (2L, new CompatItem("widget", 2)), (3L, "beta-3"),
                (4L, new CompatItem("gadget", 5)), (5L, "beta-5"), (6L, "beta-6"));
            beta.HighestSequenceNr.Should().Be(6);

            // gamma: every event was deleted, only the tombstone is left and it keeps the numbering
            var gamma = await ReplayAsync("gamma");
            gamma.Failure.Should().BeNull();
            gamma.Replayed.Should().BeEmpty();
            gamma.HighestSequenceNr.Should().Be(5);

            var unknown = await ReplayAsync("nobody");
            unknown.Replayed.Should().BeEmpty();
            unknown.HighestSequenceNr.Should().Be(0);

            Events(await ReplayAsync("alpha", from: 6, to: 7)).Should().Equal((6L, "alpha-6"), (7L, "alpha-7"));
        }

        [Fact(DisplayName = "Should_return_events_tags_and_offsets_When_querying_database_written_by_aps")]
        public async Task Should_return_events_tags_and_offsets_When_querying_database_written_by_aps()
        {
            var odd = await RunAsync(ReadJournal.CurrentEventsByTag("odd", NoOffset.Instance));
            Describe(odd).Should().Equal(
                (5L, "alpha", 5L, "alpha-5", "odd"),
                (7L, "alpha", 7L, "alpha-7", "odd"),
                (9L, "beta", 1L, "beta-1", "odd"),
                (11L, "beta", 3L, "beta-3", "blue,odd"),
                (13L, "beta", 5L, "beta-5", "odd"));

            var blue = await RunAsync(ReadJournal.CurrentEventsByTag("blue", NoOffset.Instance));
            Describe(blue).Should().Equal(
                (6L, "alpha", 6L, "alpha-6", "blue"),
                (11L, "beta", 3L, "beta-3", "blue,odd"),
                (14L, "beta", 6L, "beta-6", "blue"));

            var item = await RunAsync(ReadJournal.CurrentEventsByTag("item", Offset.Sequence(10)));
            Describe(item).Should().Equal((12L, "beta", 4L, new CompatItem("gadget", 5), "item"));

            (await RunAsync(ReadJournal.CurrentEventsByTag("nothing", NoOffset.Instance))).Should().BeEmpty();

            var all = await RunAsync(ReadJournal.CurrentAllEvents(NoOffset.Instance));
            Describe(all).Should().Equal(
                (4L, "alpha", 4L, "alpha-4", ""),
                (5L, "alpha", 5L, "alpha-5", "odd"),
                (6L, "alpha", 6L, "alpha-6", "blue"),
                (7L, "alpha", 7L, "alpha-7", "odd"),
                (8L, "alpha", 8L, "alpha-8", ""),
                (9L, "beta", 1L, "beta-1", "odd"),
                (10L, "beta", 2L, new CompatItem("widget", 2), "item"),
                (11L, "beta", 3L, "beta-3", "blue,odd"),
                (12L, "beta", 4L, new CompatItem("gadget", 5), "item"),
                (13L, "beta", 5L, "beta-5", "odd"),
                (14L, "beta", 6L, "beta-6", "blue"));

            var byPid = await RunAsync(ReadJournal.CurrentEventsByPersistenceId("alpha", 0, long.MaxValue));
            Describe(byPid).Select(e => e.SequenceNr).Should().Equal(4L, 5L, 6L, 7L, 8L);

            // Akka.Persistence.Sql 1.5.70 leaves a persistence id out when only its tombstone is left
            (await PersistenceIdsAsync()).Should().Equal("alpha", "beta");
        }

        [Fact(DisplayName = "Should_load_snapshot_When_database_was_written_by_aps")]
        public async Task Should_load_snapshot_When_database_was_written_by_aps()
        {
            var alpha = await LoadSnapshotAsync("alpha");
            alpha.Should().NotBeNull();
            alpha!.Metadata.PersistenceId.Should().Be("alpha");
            alpha.Metadata.SequenceNr.Should().Be(6);
            alpha.Snapshot.Should().Be(new CompatItem("alpha-snapshot", 6));

            (await LoadSnapshotAsync("beta")).Should().BeNull();
        }

        [Fact(DisplayName = "Should_read_back_old_and_new_data_after_restart_When_embedded_writes_and_deletes_in_aps_database")]
        public async Task Should_read_back_old_and_new_data_after_restart_When_embedded_writes_and_deletes_in_aps_database()
        {
            // numbering continues after the tombstone (gamma), after the last event (alpha, beta), and a new id starts at 1
            (await WriteAsync(Write(Evt("alpha", 9, new Tagged("alpha-9", ["odd"])), Evt("alpha", 10, "alpha-10")))).Succeeded.Should().BeTrue();
            await WriteOneAsync("gamma", 6, "gamma-6");
            await WriteOneAsync("beta", 7, new CompatItem("sprocket", 9), "item");
            await WriteOneAsync("delta", 1, "delta-1", "odd");
            await WriteOneAsync("delta", 2, "delta-2");
            await WriteOneAsync("delta", 3, "delta-3", "odd", "blue");
            await WriteOneAsync("delta", 4, "delta-4");

            // a delete above the old tombstone, a delete of every event, and snapshots saved and deleted
            await DeleteToAsync("alpha", 5);
            await DeleteToAsync("delta", 4);

            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("beta", 7, new DateTime(2026, 5, 6, 7, 8, 9, DateTimeKind.Utc)), new CompatItem("beta-snapshot", 7)), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
            SnapshotStore.Tell(new DeleteSnapshots("alpha", SnapshotSelectionCriteria.Latest), probe.Ref);
            await probe.ExpectMsgAsync<DeleteSnapshotsSuccess>(Timeout);

            // the Embedded tombstones keep their row, marked deleted, with an empty message
            Db.Query("SELECT sequence_number, deleted, length(message) FROM journal WHERE persistence_id = 'alpha' ORDER BY sequence_number")
                .Select(r => (r[0], r[1], r[2]))
                .Should().Equal(
                    ((object)5L, (object)1L, (object)0L), (6L, 0L, 9L), (7L, 0L, 9L), (8L, 0L, 9L), (9L, 0L, 9L), (10L, 0L, 10L));
            Db.Query("SELECT sequence_number, deleted, length(message) FROM journal WHERE persistence_id = 'delta'")
                .Select(r => (r[0], r[1], r[2]))
                .Should().Equal(((object)4L, (object)1L, (object)0L));

            // restart: a new actor system on the same file
            await Sys.Terminate();
            await using var restarted = new ApsCompatSpec(Db, _output);

            var alpha = await restarted.ReplayAsync("alpha");
            Events(alpha).Should().Equal((6L, "alpha-6"), (7L, "alpha-7"), (8L, "alpha-8"), (9L, "alpha-9"), (10L, "alpha-10"));
            alpha.HighestSequenceNr.Should().Be(10);

            var beta = await restarted.ReplayAsync("beta");
            Events(beta).Should().Equal(
                (1L, "beta-1"), (2L, new CompatItem("widget", 2)), (3L, "beta-3"),
                (4L, new CompatItem("gadget", 5)), (5L, "beta-5"), (6L, "beta-6"), (7L, new CompatItem("sprocket", 9)));
            beta.HighestSequenceNr.Should().Be(7);

            var gamma = await restarted.ReplayAsync("gamma");
            Events(gamma).Should().Equal((6L, "gamma-6"));
            gamma.HighestSequenceNr.Should().Be(6);

            var delta = await restarted.ReplayAsync("delta");
            delta.Replayed.Should().BeEmpty();
            delta.HighestSequenceNr.Should().Be(4);

            var all = await restarted.RunAsync(restarted.ReadJournal.CurrentAllEvents(NoOffset.Instance));
            Describe(all).Should().Equal(
                (6L, "alpha", 6L, "alpha-6", "blue"),
                (7L, "alpha", 7L, "alpha-7", "odd"),
                (8L, "alpha", 8L, "alpha-8", ""),
                (9L, "beta", 1L, "beta-1", "odd"),
                (10L, "beta", 2L, new CompatItem("widget", 2), "item"),
                (11L, "beta", 3L, "beta-3", "blue,odd"),
                (12L, "beta", 4L, new CompatItem("gadget", 5), "item"),
                (13L, "beta", 5L, "beta-5", "odd"),
                (14L, "beta", 6L, "beta-6", "blue"),
                (20L, "alpha", 9L, "alpha-9", "odd"),
                (21L, "alpha", 10L, "alpha-10", ""),
                (22L, "gamma", 6L, "gamma-6", ""),
                (23L, "beta", 7L, new CompatItem("sprocket", 9), "item"));

            var odd = await restarted.RunAsync(restarted.ReadJournal.CurrentEventsByTag("odd", NoOffset.Instance));
            Describe(odd).Select(e => (e.Offset, e.PersistenceId, e.SequenceNr)).Should().Equal(
                (7L, "alpha", 7L), (9L, "beta", 1L), (11L, "beta", 3L), (13L, "beta", 5L), (20L, "alpha", 9L));

            (await restarted.PersistenceIdsAsync()).Should().Equal("alpha", "beta", "gamma");

            (await restarted.LoadSnapshotAsync("alpha")).Should().BeNull();
            var betaSnapshot = await restarted.LoadSnapshotAsync("beta");
            betaSnapshot!.Metadata.SequenceNr.Should().Be(7);
            betaSnapshot.Snapshot.Should().Be(new CompatItem("beta-snapshot", 7));
        }
    }
}
