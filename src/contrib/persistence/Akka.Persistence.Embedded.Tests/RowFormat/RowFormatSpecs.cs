//-----------------------------------------------------------------------
// <copyright file="RowFormatSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Persistence.Journal;
using Akka.Util;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.RowFormat
{
    public class RowFormatSpec : EmbeddedSpec
    {
        public RowFormatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private RowFormatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db), nameof(RowFormatSpec), output)
        {
        }

        [Fact(DisplayName = "Should_write_values_like_Sql_When_persisting_untagged_event")]
        public async Task Should_write_values_like_Sql_When_persisting_untagged_event()
        {
            var before = DateTime.UtcNow.Ticks;
            var outcome = await WriteAsync(Write(Evt("untagged", 1, new TestEvent("hello"))));
            var after = DateTime.UtcNow.Ticks;
            outcome.Succeeded.Should().BeTrue();

            var rows = Db.Query(
                "SELECT typeof(created), created, typeof(deleted), deleted, typeof(persistence_id), persistence_id, typeof(sequence_number), sequence_number, " +
                "typeof(message), message, typeof(manifest), manifest, typeof(identifier), identifier, typeof(writer_uuid), writer_uuid FROM journal WHERE persistence_id = 'untagged'");
            rows.Should().HaveCount(1);
            var row = rows[0];

            row[0].Should().Be("integer");
            ((long)row[1]!).Should().BeInRange(before, after);
            row[2].Should().Be("integer");
            row[3].Should().Be(0L);
            row[4].Should().Be("text");
            row[5].Should().Be("untagged");
            row[6].Should().Be("integer");
            row[7].Should().Be(1L);
            row[8].Should().Be("blob");
            ((byte[])row[9]!).Should().Equal(System.Text.Encoding.UTF8.GetBytes("hello"));
            row[10].Should().Be("text");
            row[11].Should().Be("E");
            row[12].Should().Be("integer");
            row[13].Should().Be(7301L);
            row[14].Should().Be("text");
            Regex.IsMatch((string)row[15]!, "^[0-9a-f]{32}$").Should().BeTrue("the writer uuid is Guid.ToString(\"N\")");
        }

        [Fact(DisplayName = "Should_share_created_value_When_one_write_call_has_many_events")]
        public async Task Should_share_created_value_When_one_write_call_has_many_events()
        {
            var outcome = await WriteAsync(
                Write(Evt("shared", 1, new TestEvent("a")), Evt("shared", 2, new TestEvent("b")), Evt("shared", 3, new TestEvent("c"))));
            outcome.Succeeded.Should().BeTrue();

            var created = Db.Query("SELECT DISTINCT created FROM journal WHERE persistence_id = 'shared'");
            created.Should().HaveCount(1);
        }

        [Fact(DisplayName = "Should_use_representation_timestamp_When_it_is_not_zero")]
        public void Should_use_representation_timestamp_When_it_is_not_zero()
        {
            // Akka.Persistence resets the timestamp before a journal sees an actor's write (Persistent.Update drops it),
            // so this branch is only reachable by calling the codec. It stays for parity with Akka.Persistence.Sql.
            var codec = new Internal.RowCodec((Akka.Actor.ExtendedActorSystem)Sys);

            codec.Serialize(Evt("stamped", 1, new TestEvent("a"), timestamp: 123456789L), batchTicks: 42L, writerUuid: "w")
                .Created.Should().Be(123456789L);
            codec.Serialize(Evt("stamped", 2, new TestEvent("a")), batchTicks: 42L, writerUuid: "w")
                .Created.Should().Be(42L);
        }

        [Fact(DisplayName = "Should_write_one_tag_row_per_tag_When_event_is_tagged")]
        public async Task Should_write_one_tag_row_per_tag_When_event_is_tagged()
        {
            await WriteAsync(
                Write(Evt("tt", 1, new Tagged(new TestEvent("a"), new[] { "red", "blue" })), Evt("tt", 2, new TestEvent("plain"))));

            var ordering = (long)Db.Query("SELECT ordering FROM journal WHERE persistence_id = 'tt' AND sequence_number = 1")[0][0]!;
            var tags = Db.Query("SELECT ordering_id, tag, sequence_nr, persistence_id FROM tags ORDER BY tag");
            tags.Should().HaveCount(2);
            tags[0].Should().Equal(ordering, "blue", 1L, "tt");
            tags[1].Should().Equal(ordering, "red", 1L, "tt");
            Db.Query("PRAGMA table_info(journal)").Select(r => (string)r[1]!).Should().NotContain("tags");
        }

        [Fact(DisplayName = "Should_store_zero_length_blob_When_serializer_returns_empty_array")]
        public async Task Should_store_zero_length_blob_When_serializer_returns_empty_array()
        {
            await WriteAsync(Write(Evt("empty", 1, new EmptyPayload())));

            var row = Db.Query("SELECT typeof(message), length(message), manifest FROM journal WHERE persistence_id = 'empty'")[0];
            row[0].Should().Be("blob");
            row[1].Should().Be(0L);
            row[2].Should().Be("Z");

            var replay = await ReplayAsync("empty");
            replay.Replayed.Single().Payload.Should().Be(new EmptyPayload());
        }

        [Fact(DisplayName = "Should_compute_manifest_like_Sql_for_each_serializer_kind")]
        public async Task Should_compute_manifest_like_Sql_for_each_serializer_kind()
        {
            object[] payloads = [new TestEvent("a"), new ObjectManifestEvent("b"), new NoManifestEvent("c")];
            for (var i = 0; i < payloads.Length; i++)
                (await WriteAsync(Write(Evt("manifest", i + 1, payloads[i])))).Succeeded.Should().BeTrue();

            var rows = Db.Query("SELECT sequence_number, manifest, identifier FROM journal WHERE persistence_id = 'manifest' ORDER BY sequence_number");
            for (var i = 0; i < payloads.Length; i++)
            {
                var serializer = Sys.Serialization.FindSerializerForType(payloads[i].GetType());

                // Akka.Persistence.Sql: string manifest serializers use their manifest, IncludeManifest = true uses the
                // type-qualified name, anything else stores an empty string.
                var apsManifest = serializer switch
                {
                    Akka.Serialization.SerializerWithStringManifest s => s.Manifest(payloads[i]),
                    { IncludeManifest: true } => payloads[i].GetType().TypeQualifiedName(),
                    _ => string.Empty
                };

                rows[i][1].Should().Be(apsManifest);
                rows[i][2].Should().Be((long)serializer.Identifier);
            }

            rows[0][1].Should().Be("E");
            ((string)rows[1][1]!).Should().Contain(nameof(ObjectManifestEvent));
            rows[2][1].Should().Be(string.Empty);

            var replay = await ReplayAsync("manifest");
            replay.Replayed.Select(p => p.Payload).Should().Equal(payloads);
        }
    }
}
