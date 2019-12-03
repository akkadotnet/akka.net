//-----------------------------------------------------------------------
// <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2019 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2019 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
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
        private readonly ConflictNameResolver _conflictNameResolver = new ConflictNameResolver();
        private string _insertSnapshotSql;
        private string _createSnapshotTableSql;
        private string _selectSnapshotSql;
        private string _deleteSnapshotSql;
        private string _deleteSnapshotRangeSql;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configuration">TBD</param>
        /// <param name="serialization">TBD</param>
        public SqliteSnapshotQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization) : base(configuration, serialization)
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string InsertSnapshotSql => _insertSnapshotSql;

        /// <summary>
        /// TBD
        /// </summary>
        protected override string CreateSnapshotTableSql => _createSnapshotTableSql;

        protected override string DeleteSnapshotSql => _deleteSnapshotSql;

        protected override string SelectSnapshotSql => _selectSnapshotSql;

        protected override string DeleteSnapshotRangeSql => _deleteSnapshotRangeSql;

        public void Initialize(SqliteConnection connection)
        {
            if (Configuration is SqliteQueryConfiguration configuration)
            {
                var hasNameConflict = _conflictNameResolver.Resolve(connection, configuration).Result;
                if (hasNameConflict)
                {
                    Debug.WriteLine($"Found a conflict of snapshot table names. Default table name '{configuration.DefaultSnapshotTableName}' will be used.");
                }
            }
            
            _createSnapshotTableSql = $@"
                CREATE TABLE IF NOT EXISTS {Configuration.FullSnapshotTableName} (
                    {Configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {Configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {Configuration.TimestampColumnName} INTEGER(8) NOT NULL,
                    {Configuration.ManifestColumnName} VARCHAR(255) NOT NULL,
                    {Configuration.PayloadColumnName} BLOB NOT NULL,
                    {Configuration.SerializerIdColumnName} INTEGER(4),
                    PRIMARY KEY ({Configuration.PersistenceIdColumnName}, {Configuration.SequenceNrColumnName})
                );";

            _insertSnapshotSql = $@"
                UPDATE {Configuration.FullSnapshotTableName}
                SET {Configuration.TimestampColumnName} = @Timestamp, {Configuration.ManifestColumnName} = @Manifest,
                {Configuration.PayloadColumnName} = @Payload, {Configuration.SerializerIdColumnName} = @SerializerId
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId AND {Configuration.SequenceNrColumnName} = @SequenceNr;
                INSERT OR IGNORE INTO {Configuration.FullSnapshotTableName} ({Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName}, {Configuration.TimestampColumnName},
                    {Configuration.ManifestColumnName}, {Configuration.PayloadColumnName}, {Configuration.SerializerIdColumnName})
                VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Payload, @SerializerId)";
            
            _selectSnapshotSql = $@"
                SELECT {Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName}, 
                    {Configuration.TimestampColumnName}, 
                    {Configuration.ManifestColumnName}, 
                    {Configuration.PayloadColumnName},
                    {Configuration.SerializerIdColumnName}
                FROM {Configuration.FullSnapshotTableName} 
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId 
                    AND {Configuration.SequenceNrColumnName} <= @SequenceNr
                    AND {Configuration.TimestampColumnName} <= @Timestamp
                ORDER BY {Configuration.SequenceNrColumnName} DESC
                LIMIT 1";

            _deleteSnapshotSql = $@"
                DELETE FROM {Configuration.FullSnapshotTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId
                    AND {Configuration.SequenceNrColumnName} = @SequenceNr";

            _deleteSnapshotRangeSql = $@"
                DELETE FROM {Configuration.FullSnapshotTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId
                    AND {Configuration.SequenceNrColumnName} <= @SequenceNr
                    AND {Configuration.TimestampColumnName} <= @Timestamp";
        }
        
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
        protected readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="snapshotConfig">TBD</param>
        public SqliteSnapshotStore(Config snapshotConfig) : base(snapshotConfig)
        {
            var config = snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig);
            QueryExecutor = new SqliteSnapshotQueryExecutor(new SqliteQueryConfiguration(
                schemaName: null,
                snapshotTableName: config.GetString("table-name"),
                persistenceIdColumnName: "persistence_id",
                sequenceNrColumnName: "sequence_nr",
                payloadColumnName: "payload",
                manifestColumnName: "manifest",
                timestampColumnName: "created_at",
                serializerIdColumnName: "serializer_id",
                timeout: config.GetTimeSpan("connection-timeout"),
                defaultSerializer: config.GetString("serializer"),
                useSequentialAccess: config.GetBoolean("use-sequential-access"),
                // https://github.com/akkadotnet/akka.net/issues/4080
                defaultSnapshotTableName: "snapshot"),
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
            var connection = ConnectionContext.Remember(GetConnectionString());
            ((SqliteSnapshotQueryExecutor)QueryExecutor).Initialize(connection);
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
