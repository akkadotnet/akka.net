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
    public class TagTableRowFormatSpec : EmbeddedSpec
    {
        public TagTableRowFormatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private TagTableRowFormatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.TT), nameof(TagTableRowFormatSpec), output)
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
            var config = SqliteSpecConfig.Create(Db, SqliteTestMode.TT);
            var settings = Internal.JournalSettings.Create(
                config.GetConfig("akka.persistence.journal.embedded"), "akka.persistence.journal.embedded", config);
            var codec = new Internal.RowCodec((Akka.Actor.ExtendedActorSystem)Sys, settings);

            codec.Serialize(Evt("stamped", 1, new TestEvent("a"), timestamp: 123456789L), batchTicks: 42L, writerUuid: "w")
                .Created.Should().Be(123456789L);
            codec.Serialize(Evt("stamped", 2, new TestEvent("a")), batchTicks: 42L, writerUuid: "w")
                .Created.Should().Be(42L);
        }

        [Fact(DisplayName = "Should_write_one_tag_row_per_tag_When_tag_write_mode_is_TagTable")]
        public async Task Should_write_one_tag_row_per_tag_When_tag_write_mode_is_TagTable()
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

    public class CsvRowFormatSpec : EmbeddedSpec
    {
        public CsvRowFormatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private CsvRowFormatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.CSV), nameof(CsvRowFormatSpec), output)
        {
        }

        [Fact(DisplayName = "Should_write_csv_tags_with_outer_separators_When_tag_write_mode_is_Csv")]
        public async Task Should_write_csv_tags_with_outer_separators_When_tag_write_mode_is_Csv()
        {
            await WriteAsync(Write(Evt("csv", 1, new Tagged(new TestEvent("a"), new[] { "red", "blue" }))));

            var tags = (string)Db.Query("SELECT tags FROM journal WHERE persistence_id = 'csv'")[0][0]!;
            tags.Should().StartWith(";").And.EndWith(";");
            tags.Trim(';').Split(';').Should().BeEquivalentTo("red", "blue");
            Db.Query("SELECT name FROM sqlite_master WHERE name = 'tags'").Should().BeEmpty("Csv mode creates no tag table");
        }

        [Fact(DisplayName = "Should_keep_tombstone_and_tags_column_When_deleting_messages_in_Csv_mode")]
        public async Task Should_keep_tombstone_and_tags_column_When_deleting_messages_in_Csv_mode()
        {
            await WriteAsync(Write(
                Evt("csv-del", 1, new Tagged(new TestEvent("a"), new[] { "t1" })),
                Evt("csv-del", 2, new Tagged(new TestEvent("b"), new[] { "t2" })),
                Evt("csv-del", 3, new Tagged(new TestEvent("c"), new[] { "t3" }))));

            await DeleteToAsync("csv-del", 2);

            Db.Query("SELECT sequence_number, deleted, tags FROM journal WHERE persistence_id = 'csv-del' ORDER BY sequence_number")
                .Select(r => (r[0], r[1], r[2])).Should().Equal((2L, 1L, ";t2;"), (3L, 0L, ";t3;"));
            var replay = await ReplayAsync("csv-del");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(3L);
            replay.HighestSequenceNr.Should().Be(3L);
        }

        [Fact(DisplayName = "Should_write_empty_string_tags_When_event_is_untagged_in_Csv_mode")]
        public async Task Should_write_empty_string_tags_When_event_is_untagged_in_Csv_mode()
        {
            await WriteAsync(Write(Evt("csv-plain", 1, new TestEvent("a"))));

            var row = Db.Query("SELECT typeof(tags), tags FROM journal WHERE persistence_id = 'csv-plain'")[0];
            row[0].Should().Be("text");
            row[1].Should().Be(string.Empty);
        }
    }

    public class BothRowFormatSpec : EmbeddedSpec
    {
        public BothRowFormatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private BothRowFormatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.BOTH), nameof(BothRowFormatSpec), output)
        {
        }

        [Fact(DisplayName = "Should_write_column_and_rows_When_tag_write_mode_is_Both")]
        public async Task Should_write_column_and_rows_When_tag_write_mode_is_Both()
        {
            await WriteAsync(Write(Evt("both", 1, new Tagged(new TestEvent("a"), new[] { "red" }))));

            Db.Query("SELECT tags FROM journal WHERE persistence_id = 'both'")[0][0].Should().Be(";red;");
            Db.Query("SELECT tag, sequence_nr FROM tags WHERE persistence_id = 'both'").Should().HaveCount(1);
        }

        [Fact(DisplayName = "Should_delete_tag_rows_below_the_tombstone_When_deleting_messages_in_Both_mode")]
        public async Task Should_delete_tag_rows_below_the_tombstone_When_deleting_messages_in_Both_mode()
        {
            await WriteAsync(Write(
                Evt("both-del", 1, new Tagged(new TestEvent("a"), new[] { "t1" })),
                Evt("both-del", 2, new Tagged(new TestEvent("b"), new[] { "t2" })),
                Evt("both-del", 3, new Tagged(new TestEvent("c"), new[] { "t3" }))));

            await DeleteToAsync("both-del", 2);

            Db.Query("SELECT sequence_number, deleted, tags FROM journal WHERE persistence_id = 'both-del' ORDER BY sequence_number")
                .Select(r => (r[0], r[1], r[2])).Should().Equal((2L, 1L, ";t2;"), (3L, 0L, ";t3;"));
            Db.Query("SELECT tag FROM tags WHERE persistence_id = 'both-del' ORDER BY tag").Select(r => r[0]).Should().Equal("t2", "t3");
            (await ReplayAsync("both-del")).Replayed.Select(p => p.SequenceNr).Should().Equal(3L);
        }
    }

    public class NoWriterUuidRowFormatSpec : EmbeddedSpec
    {
        public NoWriterUuidRowFormatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private NoWriterUuidRowFormatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteSpecConfig.Create(db, SqliteTestMode.NW), nameof(NoWriterUuidRowFormatSpec), output)
        {
        }

        [Fact(DisplayName = "Should_omit_writer_uuid_When_column_is_off")]
        public async Task Should_omit_writer_uuid_When_column_is_off()
        {
            (await WriteAsync(Write(Evt("nw", 1, new TestEvent("a"))))).Succeeded.Should().BeTrue();

            Db.Query("PRAGMA table_info(journal)").Select(r => (string)r[1]!).Should().NotContain("writer_uuid");
            var replay = await ReplayAsync("nw");
            replay.Replayed.Should().HaveCount(1);
            replay.Replayed[0].WriterGuid.Should().BeNullOrEmpty();
        }
    }
}
