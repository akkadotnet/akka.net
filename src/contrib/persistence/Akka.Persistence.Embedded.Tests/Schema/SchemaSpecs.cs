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

        [Fact(DisplayName = "Should_create_byte_identical_sqlite_master_When_auto_initialize_runs")]
        public async Task Should_create_byte_identical_sqlite_master_When_auto_initialize_runs()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-default", SqliteSpecConfig.Create(db), InitializeAsync);

            SchemaOf(db).Should().Equal(ExpectedSchemas.Default.Select(e => (e.Type, e.Name, e.TableName, e.Sql)));
        }

        [Fact(DisplayName = "Should_not_alter_existing_tables_When_auto_initialize_runs_twice")]
        public async Task Should_not_alter_existing_tables_When_auto_initialize_runs_twice()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-twice-1", SqliteSpecConfig.Create(db), InitializeAsync);
            var first = SchemaOf(db);

            await WithSystemAsync("schema-twice-2", SqliteSpecConfig.Create(db), InitializeAsync);

            SchemaOf(db).Should().Equal(first);
        }

        [Fact(DisplayName = "Should_fail_init_naming_missing_columns_When_a_required_column_is_absent")]
        public async Task Should_fail_init_naming_missing_columns_When_a_required_column_is_absent()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-missing", SqliteSpecConfig.Create(db), InitializeAsync);
            db.Execute("ALTER TABLE journal DROP COLUMN writer_uuid");
            db.Execute("DROP INDEX tags_persistence_id_sequence_nr_idx");
            db.Execute("ALTER TABLE tags DROP COLUMN sequence_nr");

            var config = SqliteSpecConfig.Create(db);
            var settings = JournalSettings.Create(config.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ConnectionString);
            connection.Open();

            Assert.Throws<SqliteSchemaException>(() => SqliteSchema.VerifyJournalSchema(connection, settings)).Message
                .Should().Be("Table [journal] is missing column(s): writer_uuid. This plugin never alters tables.");

            db.Execute("ALTER TABLE journal ADD COLUMN writer_uuid NVARCHAR(128) NULL");
            Assert.Throws<SqliteSchemaException>(() => SqliteSchema.VerifyJournalSchema(connection, settings)).Message
                .Should().Be("Table [tags] is missing column(s): sequence_nr. This plugin never alters tables.");
        }

        [Fact(DisplayName = "Should_accept_the_table_When_it_has_extra_columns")]
        public async Task Should_accept_the_table_When_it_has_extra_columns()
        {
            using var db = new SqliteTestDb();
            await WithSystemAsync("schema-extra", SqliteSpecConfig.Create(db), InitializeAsync);
            db.Execute("ALTER TABLE journal ADD COLUMN tags NVARCHAR(100) NULL");
            db.Execute("ALTER TABLE journal ADD COLUMN something_else TEXT NULL");

            await WithSystemAsync("schema-extra-2", SqliteSpecConfig.Create(db), InitializeAsync);
        }
    }

    public class OrderingColumnSchemaSpec
    {
        private const string Columns =
            "created BIGINT NOT NULL, deleted BIT NOT NULL, persistence_id NVARCHAR(255) NOT NULL, sequence_number BIGINT NOT NULL, " +
            "message VARBINARY NOT NULL, tags NVARCHAR(100) NULL, manifest NVARCHAR(500) NULL, identifier INTEGER NULL, writer_uuid NVARCHAR(128) NULL";

        [Theory(DisplayName = "Should_fail_naming_the_ordering_column_When_it_is_not_an_INTEGER_PRIMARY_KEY")]
        [InlineData("ordering INTEGER NOT NULL")]
        [InlineData("ordering BIGINT NOT NULL PRIMARY KEY")]
        [InlineData("ordering INTEGER NOT NULL UNIQUE")]
        public void Should_fail_naming_the_ordering_column_When_it_is_not_an_INTEGER_PRIMARY_KEY(string orderingColumn)
        {
            using var db = new SqliteTestDb();
            db.Execute($"CREATE TABLE journal ({orderingColumn}, {Columns})");
            var config = SqliteSpecConfig.Create(db);
            var settings = JournalSettings.Create(config.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ConnectionString);
            connection.Open();

            var failure = Assert.Throws<SqliteSchemaException>(() => SqliteSchema.VerifyJournalSchema(connection, settings));

            failure.Message.Should().Be("Column [ordering] must be INTEGER PRIMARY KEY (rowid alias).");
        }

        [Fact(DisplayName = "Should_accept_the_ordering_column_When_it_is_an_INTEGER_PRIMARY_KEY_without_autoincrement")]
        public void Should_accept_the_ordering_column_When_it_is_an_INTEGER_PRIMARY_KEY_without_autoincrement()
        {
            using var db = new SqliteTestDb();
            db.Execute($"CREATE TABLE journal (ordering INTEGER NOT NULL PRIMARY KEY, {Columns})");
            db.Execute("CREATE TABLE tags (ordering_id INTEGER NOT NULL, tag NVARCHAR(64) NOT NULL, sequence_nr INTEGER NOT NULL, persistence_id NVARCHAR(255) NOT NULL, PRIMARY KEY (ordering_id, tag))");
            var config = SqliteSpecConfig.Create(db);
            var settings = JournalSettings.Create(config.GetConfig(SqlitePersistence.JournalPluginId), SqlitePersistence.JournalPluginId);
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.FilePath, Pooling = false }.ConnectionString);
            connection.Open();

            var warnings = SqliteSchema.VerifyJournalSchema(connection, settings);

            warnings.Should().ContainSingle("only the unique index on the persistence id is missing").Which.Should().Contain("UNIQUE index");
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
                SqliteSpecConfig.Create(db, """
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

    /// <summary>
    /// Tables created by hand from the Akka.Persistence.Sql docs, with auto-initialize off. The docs journal table has a
    /// nullable <c>tags</c> column that this plugin never writes.
    /// </summary>
    public class DocsDdlSpec : EmbeddedSpec
    {
        public DocsDdlSpec(ITestOutputHelper output) : this(new SqliteTestDb(), output)
        {
        }

        private DocsDdlSpec(SqliteTestDb db, ITestOutputHelper output)
            : base(
                db,
                SqliteSpecConfig.Create(db, """
                    akka.persistence.journal.embedded.auto-initialize = false
                    akka.persistence.snapshot-store.embedded.auto-initialize = false
                    """),
                nameof(DocsDdlSpec),
                output)
        {
            foreach (var script in new[] { "journal", "journal-tags", "snapshot" })
                db.Execute(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "docs-ddl", $"{script}.sql")));
        }

        [Fact(DisplayName = "Should_work_on_docs_ddl_schema_When_auto_initialize_is_off")]
        public async Task Should_work_on_docs_ddl_schema_When_auto_initialize_is_off()
        {
            var materializer = Sys.Materializer();
            (await WriteAsync(Write(
                Evt("docs", 1, new Tagged(new TestEvent("a"), new[] { "red" })),
                Evt("docs", 2, new TestEvent("b"))))).Succeeded.Should().BeTrue();
            Db.Query("PRAGMA table_info(journal)").Select(r => (string)r[1]!).Should().Contain("tags");
            Db.Query("SELECT tags FROM journal").Should().OnlyContain(r => r[0] == null, "the plugin leaves the docs tags column NULL");

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
}
