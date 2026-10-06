//-----------------------------------------------------------------------
// <copyright file="SnapshotBehaviorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Snapshot
{
    public class SnapshotBehaviorSpec : EmbeddedSpec
    {
        // seq 3 was taken at T0, seq 5 ten minutes later
        private static readonly DateTime T0 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime T5 = T0.AddMinutes(10);

        public SnapshotBehaviorSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SnapshotBehaviorSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db), nameof(SnapshotBehaviorSpec), output)
        {
        }

        private async Task SaveAsync(string pid, long seq, DateTime timestamp, string value)
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata(pid, seq, timestamp), new TestEvent(value)), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
        }

        private async Task SaveBothAsync(string pid)
        {
            await SaveAsync(pid, 3, T0, "s3");
            await SaveAsync(pid, 5, T5, "s5");
        }

        private async Task<SelectedSnapshot?> LoadAsync(string pid, SnapshotSelectionCriteria criteria)
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new LoadSnapshot(pid, criteria, long.MaxValue), probe.Ref);
            return (await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout)).Snapshot;
        }

        private long[] Rows(string pid)
            => Db.Query($"SELECT sequence_number FROM snapshot WHERE persistence_id = '{pid}' ORDER BY sequence_number")
                .Select(r => (long)r[0]!).ToArray();

        private async Task DeleteByCriteriaAsync(string pid, SnapshotSelectionCriteria criteria)
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new DeleteSnapshots(pid, criteria), probe.Ref);
            await probe.ExpectMsgAsync<DeleteSnapshotsSuccess>(Timeout);
        }

        private async Task DeleteByMetadataAsync(string pid, long seq, DateTime timestamp)
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new DeleteSnapshot(new SnapshotMetadata(pid, seq, timestamp)), probe.Ref);
            await probe.ExpectMsgAsync<DeleteSnapshotSuccess>(Timeout);
        }

        [Fact(DisplayName = "Should_upsert_When_saving_same_persistence_id_and_sequence_twice")]
        public async Task Should_upsert_When_saving_same_persistence_id_and_sequence_twice()
        {
            await SaveAsync("upsert", 3, T0, "first");
            await SaveAsync("upsert", 3, T0.AddMinutes(1), "second");

            Rows("upsert").Should().Equal(3L);
            var loaded = await LoadAsync("upsert", SnapshotSelectionCriteria.Latest);
            loaded!.Snapshot.Should().Be(new TestEvent("second"));
            loaded.Metadata.Timestamp.Should().Be(T0.AddMinutes(1));
        }

        [Fact(DisplayName = "Should_load_latest_When_criteria_have_no_bounds")]
        public async Task Should_load_latest_When_criteria_have_no_bounds()
        {
            await SaveBothAsync("load-latest");

            (await LoadAsync("load-latest", SnapshotSelectionCriteria.Latest))!.Metadata.SequenceNr.Should().Be(5L);
        }

        [Fact(DisplayName = "Should_load_below_bound_When_only_max_sequence_is_given")]
        public async Task Should_load_below_bound_When_only_max_sequence_is_given()
        {
            await SaveBothAsync("load-seq");

            (await LoadAsync("load-seq", new SnapshotSelectionCriteria(4)))!.Metadata.SequenceNr.Should().Be(3L);
        }

        [Fact(DisplayName = "Should_load_below_bound_When_only_max_timestamp_is_given")]
        public async Task Should_load_below_bound_When_only_max_timestamp_is_given()
        {
            await SaveBothAsync("load-ts");

            (await LoadAsync("load-ts", new SnapshotSelectionCriteria(long.MaxValue, T0.AddMinutes(1))))!.Metadata.SequenceNr.Should().Be(3L);
        }

        [Fact(DisplayName = "Should_load_below_bounds_When_sequence_and_timestamp_are_given")]
        public async Task Should_load_below_bounds_When_sequence_and_timestamp_are_given()
        {
            await SaveBothAsync("load-both");

            (await LoadAsync("load-both", new SnapshotSelectionCriteria(5, T0.AddMinutes(1))))!.Metadata.SequenceNr.Should().Be(3L);
        }

        [Fact(DisplayName = "Should_return_nothing_When_no_snapshot_matches")]
        public async Task Should_return_nothing_When_no_snapshot_matches()
        {
            await SaveBothAsync("load-none");

            (await LoadAsync("load-none", new SnapshotSelectionCriteria(2))).Should().BeNull();
        }

        [Fact(DisplayName = "Should_delete_by_sequence_When_only_max_sequence_is_given")]
        public async Task Should_delete_by_sequence_When_only_max_sequence_is_given()
        {
            await SaveBothAsync("del-seq");
            await DeleteByCriteriaAsync("del-seq", new SnapshotSelectionCriteria(3));

            Rows("del-seq").Should().Equal(5L);
        }

        [Fact(DisplayName = "Should_delete_by_timestamp_When_only_max_timestamp_is_given")]
        public async Task Should_delete_by_timestamp_When_only_max_timestamp_is_given()
        {
            await SaveBothAsync("del-ts");
            await DeleteByCriteriaAsync("del-ts", new SnapshotSelectionCriteria(long.MaxValue, T0.AddMinutes(1)));

            Rows("del-ts").Should().Equal(5L);
        }

        [Fact(DisplayName = "Should_delete_by_sequence_and_timestamp_When_both_are_given")]
        public async Task Should_delete_by_sequence_and_timestamp_When_both_are_given()
        {
            await SaveBothAsync("del-both");
            await DeleteByCriteriaAsync("del-both", new SnapshotSelectionCriteria(4, T5.AddMinutes(1)));

            Rows("del-both").Should().Equal(5L);
        }

        [Fact(DisplayName = "Should_delete_everything_When_criteria_are_Latest")]
        public async Task Should_delete_everything_When_criteria_are_Latest()
        {
            await SaveBothAsync("del-all");
            await DeleteByCriteriaAsync("del-all", SnapshotSelectionCriteria.Latest);

            Rows("del-all").Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_ignore_min_bounds_When_deleting_by_criteria")]
        public async Task Should_ignore_min_bounds_When_deleting_by_criteria()
        {
            await SaveBothAsync("del-min");

            // Akka.Persistence.Sql ignores MinSequenceNr and MinTimestamp, so this deletes both rows
            await DeleteByCriteriaAsync("del-min", new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue, 4, T0.AddMinutes(1)));

            Rows("del-min").Should().BeEmpty();
        }

        [Fact(DisplayName = "Should_delete_one_snapshot_When_metadata_matches_sequence_and_timestamp")]
        public async Task Should_delete_one_snapshot_When_metadata_matches_sequence_and_timestamp()
        {
            await SaveBothAsync("del-meta");
            await DeleteByMetadataAsync("del-meta", 3, T0);

            Rows("del-meta").Should().Equal(5L);
        }

        [Fact(DisplayName = "Should_delete_one_snapshot_When_metadata_timestamp_is_MinValue")]
        public async Task Should_delete_one_snapshot_When_metadata_timestamp_is_MinValue()
        {
            await SaveBothAsync("del-meta-min");
            await DeleteByMetadataAsync("del-meta-min", 5, DateTime.MinValue);

            Rows("del-meta-min").Should().Equal(3L);
        }

        [Fact(DisplayName = "Should_keep_snapshot_When_metadata_timestamp_is_older_than_stored")]
        public async Task Should_keep_snapshot_When_metadata_timestamp_is_older_than_stored()
        {
            await SaveBothAsync("del-meta-old");
            await DeleteByMetadataAsync("del-meta-old", 5, T0);

            Rows("del-meta-old").Should().Equal(3L, 5L);
        }

        [Fact(DisplayName = "Should_store_snapshot_row_with_the_same_storage_classes_as_Sql")]
        public async Task Should_store_snapshot_row_with_the_same_storage_classes_as_Sql()
        {
            await SaveAsync("classes", 3, T0, "s3");

            // Akka.Persistence.Sql 1.5.70 capture: text, integer, integer, blob, text (''), integer
            var row = Db.Query(
                "SELECT typeof(persistence_id), typeof(sequence_number), typeof(created), typeof(snapshot), typeof(manifest), typeof(serializer_id), " +
                "created, manifest, serializer_id FROM snapshot WHERE persistence_id = 'classes'")[0];
            row.Take(6).Should().Equal("text", "integer", "integer", "blob", "text", "integer");
            row[6].Should().Be(T0.Ticks);
            row[7].Should().Be("E");
            row[8].Should().Be(7301L);
        }

        [Fact(DisplayName = "Should_fail_the_save_cleanly_When_the_snapshot_cannot_be_serialized")]
        public async Task Should_fail_the_save_cleanly_When_the_snapshot_cannot_be_serialized()
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("bad-snap", 1, T0), new UnserializableEvent("x")), probe.Ref);

            var failure = await probe.ExpectMsgAsync<SaveSnapshotFailure>(Timeout);

            failure.Cause.Should().BeOfType<InvalidOperationException>();
            Rows("bad-snap").Should().BeEmpty();
            // the store is still alive
            await SaveAsync("bad-snap", 2, T0, "fine");
            Rows("bad-snap").Should().Equal(2L);
        }

        [Fact(DisplayName = "Should_return_utc_timestamp_When_loading")]
        public async Task Should_return_utc_timestamp_When_loading()
        {
            await SaveAsync("utc", 3, T0, "s");

            var loaded = await LoadAsync("utc", SnapshotSelectionCriteria.Latest);

            loaded!.Metadata.Timestamp.Kind.Should().Be(DateTimeKind.Utc);
            loaded.Metadata.Timestamp.Ticks.Should().Be(T0.Ticks);
        }
    }
}
