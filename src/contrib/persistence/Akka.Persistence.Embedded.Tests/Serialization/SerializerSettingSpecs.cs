//-----------------------------------------------------------------------
// <copyright file="SerializerSettingSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Serialization
{
    /// <summary>The <c>serializer</c> setting: a binding wins, otherwise the named serializer replaces the System.Object fallback.</summary>
    public class SerializerSettingSpec : EmbeddedSpec
    {
        private const string SettingHocon = """
            akka.persistence.journal.embedded.serializer = "unbound-event"
            akka.persistence.snapshot-store.embedded.serializer = "unbound-event"
            """;

        public SerializerSettingSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private SerializerSettingSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SettingHocon), nameof(SerializerSettingSpec), output)
        {
        }

        [Fact(DisplayName = "Should_write_unbound_event_with_named_serializer_When_serializer_setting_is_set")]
        public async Task Should_write_unbound_event_with_named_serializer_When_serializer_setting_is_set()
        {
            var outcome = await WriteAsync(Write(Evt("journal-unbound", 1, new UnboundEvent("named"))));
            outcome.Succeeded.Should().BeTrue();

            var row = Db.Query("SELECT identifier, manifest, message FROM journal WHERE persistence_id = 'journal-unbound'").Single();
            row[0].Should().Be(7305L);
            row[1].Should().Be("U");
            ((byte[])row[2]!).Should().Equal(System.Text.Encoding.UTF8.GetBytes("named"));

            var replay = await ReplayAsync("journal-unbound");
            replay.Failure.Should().BeNull();
            replay.Replayed.Single().Payload.Should().Be(new UnboundEvent("named"));
        }

        [Fact(DisplayName = "Should_keep_serialization_binding_When_event_type_is_bound_and_serializer_setting_is_set")]
        public async Task Should_keep_serialization_binding_When_event_type_is_bound_and_serializer_setting_is_set()
        {
            var outcome = await WriteAsync(Write(Evt("journal-bound", 1, new TestEvent("bound"))));
            outcome.Succeeded.Should().BeTrue();

            Db.Query("SELECT identifier FROM journal WHERE persistence_id = 'journal-bound'").Single()[0].Should().Be(7301L);

            var replay = await ReplayAsync("journal-bound");
            replay.Replayed.Single().Payload.Should().Be(new TestEvent("bound"));
        }

        [Fact(DisplayName = "Should_save_unbound_snapshot_with_named_serializer_When_serializer_setting_is_set")]
        public async Task Should_save_unbound_snapshot_with_named_serializer_When_serializer_setting_is_set()
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("snap-unbound", 1, new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)), new UnboundEvent("named")), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);

            Db.Query("SELECT serializer_id, manifest FROM snapshot WHERE persistence_id = 'snap-unbound'")
                .Single().Should().Equal(7305L, "U");

            SnapshotStore.Tell(new LoadSnapshot("snap-unbound", SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            var loaded = await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout);
            loaded.Snapshot!.Snapshot.Should().Be(new UnboundEvent("named"));
        }

        [Fact(DisplayName = "Should_keep_serialization_binding_When_snapshot_type_is_bound_and_serializer_setting_is_set")]
        public async Task Should_keep_serialization_binding_When_snapshot_type_is_bound_and_serializer_setting_is_set()
        {
            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("snap-bound", 1, new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc)), new TestEvent("bound")), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);

            Db.Query("SELECT serializer_id FROM snapshot WHERE persistence_id = 'snap-bound'").Single()[0].Should().Be(7301L);

            SnapshotStore.Tell(new LoadSnapshot("snap-bound", SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            var loaded = await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout);
            loaded.Snapshot!.Snapshot.Should().Be(new TestEvent("bound"));
        }
    }
}
