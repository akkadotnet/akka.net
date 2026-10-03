//-----------------------------------------------------------------------
// <copyright file="SchemaSpecs.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Streams;
using Akka.Streams.Dsl;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Akka.Persistence.Embedded.Tests.Schema
{
    public class SchemaCreationSpec
    {
        private static async Task WithSystemAsync(string name, Config config, Func<ExtendedActorSystem, Task> body)
        {
            var system = (ExtendedActorSystem)ActorSystem.Create(name, config);
            try
            {
                await body(system);
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static async Task InitializeAsync(ExtendedActorSystem system)
        {
            var persistence = Persistence.Instance.Apply(system);
            await persistence.JournalFor(null).Ask<Initialized>(EnsureInitialized.Instance, TimeSpan.FromSeconds(10));
            await persistence.SnapshotStoreFor(null).Ask<Initialized>(EnsureInitialized.Instance, TimeSpan.FromSeconds(10));
        }

        private static (string, string, string, string?)[] SchemaOf(SqliteTestDb db)
            => db.Query("SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY type, name")
                .Select(r => ((string)r[0]!, (string)r[1]!, (string)r[2]!, (string?)r[3])).ToArray();

        [Theory(DisplayName = "Should_create_byte_identical_sqlite_master_When_auto_initialize_runs")]
        [InlineData(SqliteTestMode.TT)]
        [InlineData(SqliteTestMode.CSV)]
        [InlineData(SqliteTestMode.BOTH)]
        [InlineData(SqliteTestMode.DC)]
        [InlineData(SqliteTestMode.NW)]
        public async Task Should_create_byte_identical_sqlite_master_When_auto_initialize_runs(SqliteTestMode mode)
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync($"schema-{mode}", SqliteSpecConfig.Create(db, mode), InitializeAsync);

            var expected = ExpectedSchemas.For(mode);
            SchemaOf(db).Should().Equal(expected.Select(e => (e.Type, e.Name, e.TableName, e.Sql)));
        }

        [Fact(DisplayName = "Should_not_alter_existing_tables_When_auto_initialize_runs_twice")]
        public async Task Should_not_alter_existing_tables_When_auto_initialize_runs_twice()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-twice-1", SqliteSpecConfig.Create(db, SqliteTestMode.TT), InitializeAsync);
            var first = SchemaOf(db);

            await WithSystemAsync("schema-twice-2", SqliteSpecConfig.Create(db, SqliteTestMode.TT), InitializeAsync);

            SchemaOf(db).Should().Equal(first);
        }

        [Fact(DisplayName = "Should_fail_init_naming_missing_columns_When_tags_column_is_absent_in_Csv_mode")]
        public async Task Should_fail_init_naming_missing_columns_When_tags_column_is_absent_in_Csv_mode()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-missing", SqliteSpecConfig.Create(db, SqliteTestMode.TT), InitializeAsync);

            var csv = SqliteSpecConfig.Create(db, SqliteTestMode.CSV);
            var settings = JournalSettings.Create(csv.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId, csv);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ConnectionString);
            connection.Open();

            var failure = Assert.Throws<SqliteSchemaException>(() => SqliteSchema.EnsureJournalSchema(connection, settings, false, false));

            failure.Message.Should().Be(
                "Table [journal] is missing column(s): tags (required by tag-write-mode = Csv). This plugin never alters tables.");

            // the same table also fails when only the read side wants the column
            var tagTable = SqliteSpecConfig.Create(db, SqliteTestMode.TT);
            var tagTableSettings = JournalSettings.Create(tagTable.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId, tagTable);
            Assert.Throws<SqliteSchemaException>(() => SqliteSchema.VerifyJournalSchema(connection, tagTableSettings, requireTagsColumnForReads: true))
                .Message.Should().Be("Table [journal] is missing column(s): tags (required by tag-read-mode = Csv). This plugin never alters tables.");
        }

        [Fact(DisplayName = "Should_blame_writer_uuid_setting_When_writer_uuid_column_is_absent")]
        public async Task Should_blame_writer_uuid_setting_When_writer_uuid_column_is_absent()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-no-uuid", SqliteSpecConfig.Create(db, SqliteTestMode.NW), InitializeAsync);

            var withUuid = SqliteSpecConfig.Create(db, SqliteTestMode.TT);
            var settings = JournalSettings.Create(withUuid.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId, withUuid);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ConnectionString);
            connection.Open();

            Assert.Throws<SqliteSchemaException>(() => SqliteSchema.VerifyJournalSchema(connection, settings))
                .Message.Should().Be("Table [journal] is missing column(s): writer_uuid (required by use-writer-uuid-column = true). This plugin never alters tables.");
        }
    }

    public class MissingUniqueIndexSpec : EmbeddedSpec
    {
        public MissingUniqueIndexSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private MissingUniqueIndexSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, SqliteTestMode.TT, """
                    akka.persistence.journal.embedded.auto-initialize = false
                    akka.persistence.snapshot-store.embedded.auto-initialize = false
                    """),
                nameof(MissingUniqueIndexSpec),
                output)
        {
            db.Execute(
                "CREATE TABLE journal (ordering INTEGER PRIMARY KEY AUTOINCREMENT, deleted INTEGER NOT NULL, persistence_id TEXT NOT NULL, " +
                "sequence_number INTEGER NOT NULL, created INTEGER NOT NULL, message BLOB NOT NULL, identifier INTEGER, manifest TEXT, writer_uuid TEXT);" +
                "CREATE TABLE tags (ordering_id INTEGER NOT NULL, tag TEXT NOT NULL, sequence_nr INTEGER NOT NULL, persistence_id TEXT NOT NULL, PRIMARY KEY (ordering_id, tag));");
        }

        [Fact(DisplayName = "Should_log_warning_When_unique_index_is_missing")]
        public async Task Should_log_warning_When_unique_index_is_missing()
        {
            await EventFilter.Warning(contains: "has no UNIQUE index on (persistence_id, sequence_number)")
                .ExpectOneAsync(async () => await InitializeJournalAsync());
        }
    }

    /// <summary>Tables created by hand from the Akka.Persistence.Sql docs, with auto-initialize off.</summary>
    public abstract class DocsDdlSpecBase : EmbeddedSpec
    {
        protected DocsDdlSpecBase(SqliteTestDb db, SqliteTestMode mode, string name, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, mode, """
                    akka.persistence.journal.embedded.auto-initialize = false
                    akka.persistence.snapshot-store.embedded.auto-initialize = false
                    """),
                name,
                output)
        {
            foreach (var script in new[] { "journal", "journal-tags", "snapshot" }.Concat(mode == SqliteTestMode.DC ? ["metadata"] : Array.Empty<string>()))
                db.Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "docs-ddl", $"{script}.sql")));
        }

        protected async Task RunScenarioAsync()
        {
            var materializer = Sys.Materializer();
            (await WriteAsync(Write(
                Evt("docs", 1, new Tagged(new TestEvent("a"), new[] { "red" })),
                Evt("docs", 2, new TestEvent("b"))))).Succeeded.Should().BeTrue();

            var replay = await ReplayAsync("docs");
            replay.Replayed.Select(p => p.SequenceNr).Should().Equal(1L, 2L);
            replay.HighestSequenceNr.Should().Be(2L);

            var readJournal = Sys.ReadJournalFor<SqliteReadJournal>(SqliteReadJournal.Identifier);
            var tagged = await readJournal.CurrentEventsByTag("red", NoOffset.Instance).RunWith(Sink.Seq<EventEnvelope>(), materializer).WaitAsync(Timeout);
            tagged.Select(e => e.SequenceNr).Should().Equal(1L);
            tagged[0].Tags.Should().Equal("red");

            await DeleteToAsync("docs", 1);
            (await ReplayAsync("docs")).Replayed.Select(p => p.SequenceNr).Should().Equal(2L);

            var probe = CreateTestProbe();
            SnapshotStore.Tell(new SaveSnapshot(new SnapshotMetadata("docs", 2, DateTime.UtcNow), new TestEvent("snap")), probe.Ref);
            await probe.ExpectMsgAsync<SaveSnapshotSuccess>(Timeout);
            SnapshotStore.Tell(new LoadSnapshot("docs", SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            (await probe.ExpectMsgAsync<LoadSnapshotResult>(Timeout)).Snapshot.Snapshot.Should().Be(new TestEvent("snap"));
        }
    }

    public class DocsDdlTagTableSpec : DocsDdlSpecBase
    {
        public DocsDdlTagTableSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private DocsDdlTagTableSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteTestMode.TT, nameof(DocsDdlTagTableSpec), output)
        {
        }

        [Fact(DisplayName = "Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_TagTable_mode")]
        public Task Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_TagTable_mode() => RunScenarioAsync();
    }

    public class DocsDdlCsvSpec : DocsDdlSpecBase
    {
        public DocsDdlCsvSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private DocsDdlCsvSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteTestMode.CSV, nameof(DocsDdlCsvSpec), output)
        {
        }

        [Fact(DisplayName = "Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_Csv_mode")]
        public Task Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_Csv_mode() => RunScenarioAsync();
    }

    public class DocsDdlDeleteCompatSpec : DocsDdlSpecBase
    {
        public DocsDdlDeleteCompatSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private DocsDdlDeleteCompatSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(db, SqliteTestMode.DC, nameof(DocsDdlDeleteCompatSpec), output)
        {
        }

        [Fact(DisplayName = "Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_delete_compatibility_mode")]
        public Task Should_work_on_docs_ddl_schema_When_auto_initialize_is_off_in_delete_compatibility_mode() => RunScenarioAsync();
    }
}
