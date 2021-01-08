//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akka.Configuration;
using Akka.Persistence.Sql.Common.Snapshot;

namespace Akka.Persistence.Sqlite.Snapshot
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SqliteSnapshotQueryExecutor : AbstractQueryExecutor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configuration">TBD</param>
        /// <param name="serialization">TBD</param>
        public SqliteSnapshotQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization) : base(configuration, serialization)
        {
            CreateSnapshotTableSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullSnapshotTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {configuration.TimestampColumnName} INTEGER(8) NOT NULL,
                    {configuration.ManifestColumnName} VARCHAR(255) NOT NULL,
                    {configuration.PayloadColumnName} BLOB NOT NULL,
                    {configuration.SerializerIdColumnName} INTEGER(4),
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";

            InsertSnapshotSql = $@"
                UPDATE {configuration.FullSnapshotTableName}
                SET {configuration.TimestampColumnName} = @Timestamp, {configuration.ManifestColumnName} = @Manifest,
                {configuration.PayloadColumnName} = @Payload, {configuration.SerializerIdColumnName} = @SerializerId
                WHERE {configuration.PersistenceIdColumnName} = @PersistenceId AND {configuration.SequenceNrColumnName} = @SequenceNr;
                INSERT OR IGNORE INTO {configuration.FullSnapshotTableName} ({configuration.PersistenceIdColumnName},
                    {configuration.SequenceNrColumnName}, {configuration.TimestampColumnName},
                    {configuration.ManifestColumnName}, {configuration.PayloadColumnName}, {configuration.SerializerIdColumnName})
                VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Payload, @SerializerId)";
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string InsertSnapshotSql { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        protected override string CreateSnapshotTableSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <returns>TBD</returns>
        protected override DbCommand CreateCommand(DbConnection connection)
        {
            return new SqliteCommand { Connection = (SqliteConnection)connection };
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timestamp">TBD</param>
        /// <param name="command">TBD</param>
        /// <returns>TBD</returns>
        protected override void SetTimestampParameter(DateTime timestamp, DbCommand command) => AddParameter(command, "@Timestamp", DbType.Int64, timestamp.Ticks);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reader">TBD</param>
        /// <returns>TBD</returns>
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

    /// <summary>
    /// TBD
    /// </summary>
    public class SqliteSnapshotStore : SqlSnapshotStore
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected static readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="snapshotConfig">TBD</param>
        public SqliteSnapshotStore(Config snapshotConfig) : 
            base(snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig))
        {
            var config = snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig);
            QueryExecutor = new SqliteSnapshotQueryExecutor(new QueryConfiguration(
                schemaName: null,
                snapshotTableName: config.GetString("table-name"),
                persistenceIdColumnName: "persistence_id",
                sequenceNrColumnName: "sequence_nr",
                payloadColumnName: "payload",
                manifestColumnName: "manifest",
                timestampColumnName: "created_at",
                serializerIdColumnName: "serializer_id",
                timeout: config.GetTimeSpan("connection-timeout"),
                defaultSerializer: config.GetString("serializer", null),
                useSequentialAccess: config.GetBoolean("use-sequential-access", false)),
                Context.System.Serialization);
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override ISnapshotQueryExecutor QueryExecutor { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected override DbConnection CreateDbConnection(string connectionString)
        {
            return new SqliteConnection(connectionString);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            ConnectionContext.Remember(GetConnectionString());
            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            ConnectionContext.Forget(GetConnectionString());
        }
    }
}
