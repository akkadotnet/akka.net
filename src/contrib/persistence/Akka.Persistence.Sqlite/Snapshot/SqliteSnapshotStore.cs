//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Data.SQLite;
using Akka.Configuration;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    public class SqliteSnapshotQueryExecutor : AbstractQueryExecutor
    {
        public SqliteSnapshotQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization) : base(configuration, serialization)
        {
            CreateSnapshotTableSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullSnapshotTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {configuration.TimestampColumnName} INTEGER(8) NOT NULL,
                    {configuration.ManifestColumnName} VARCHAR(255) NOT NULL,
                    {configuration.PayloadColumnName} BLOB NOT NULL,
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";
        }

        protected override string CreateSnapshotTableSql { get; }
        protected override DbCommand CreateCommand(DbConnection connection)
        {
            return new SQLiteCommand((SQLiteConnection)connection);
        }

        protected override void SetTimestampParameter(DateTime timestamp, DbCommand command) => AddParameter(command, "@Timestamp", DbType.Int64, timestamp.Ticks);

        protected override SelectedSnapshot ReadSnapshot(DbDataReader reader)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var timestamp = new DateTime(reader.GetInt64(2));

            var metadata = new SnapshotMetadata(persistenceId, sequenceNr, timestamp);
            var snapshot = GetSnapshot(reader);

            return new SelectedSnapshot(metadata, snapshot);
        }
    }

    public class SqliteSnapshotStore : SqlSnapshotStore
    {
        protected readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);

        public SqliteSnapshotStore(Config snapshotConfig) : base(snapshotConfig)
        {
            var config = snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig);
            QueryExecutor = new SqliteSnapshotQueryExecutor(new QueryConfiguration(
                schemaName: null,
                snapshotTableName: "snapshot",
                persistenceIdColumnName: "persistence_id",
                sequenceNrColumnName: "sequence_nr",
                payloadColumnName: "payload",
                manifestColumnName: "manifest",
                timestampColumnName: "created_at",
                timeout: config.GetTimeSpan("connection-timeout")), 
                Context.System.Serialization);
        }


        public override ISnapshotQueryExecutor QueryExecutor { get; }

        protected override DbConnection CreateDbConnection(string connectionString)
        {
            return new SQLiteConnection(connectionString);
        }

        protected override void PreStart()
        {
            ConnectionContext.Remember(GetConnectionString());
            base.PreStart();
        }

        protected override void PostStop()
        {
            base.PostStop();
            ConnectionContext.Forget(GetConnectionString());
        }
    }
}