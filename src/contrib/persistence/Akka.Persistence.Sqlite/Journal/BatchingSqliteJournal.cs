//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Persistence.Sql.Common.Extensions;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class BatchingSqliteJournalSetup : BatchingSqlJournalSetup
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqliteJournalSetup" /> class.
        /// </summary>
        /// <param name="config">Config object used to obtain Journal settings</param>
        public BatchingSqliteJournalSetup(Config config) : base(config, new QueryConfiguration(
                    schemaName: null,
                    journalEventsTableName: config.GetString("table-name"),
                    metaTableName: config.GetString("metadata-table-name"),
                    persistenceIdColumnName: "persistence_id",
                    sequenceNrColumnName: "sequence_nr",
                    payloadColumnName: "payload",
                    manifestColumnName: "manifest",
                    timestampColumnName: "timestamp",
                    isDeletedColumnName: "is_deleted",
                    tagsColumnName: "tags",
                    orderingColumnName: "ordering",
                    serializerIdColumnName: "serializer_id",
                    timeout: config.GetTimeSpan("connection-timeout"),
                    defaultSerializer: config.GetString("serializer"),
                    useSequentialAccess: config.GetBoolean("use-sequential-access"),
                    readIsolationLevel: config.GetIsolationLevel("read-isolation-level"),
                    writeIsolationLevel: config.GetIsolationLevel("write-isolation-level")))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqliteJournalSetup" /> class.
        /// </summary>
        /// <param name="connectionString">The connection string used to connect to the database.</param>
        /// <param name="maxConcurrentOperations">The maximum number of batch operations allowed to be executed at the same time.</param>
        /// <param name="maxBatchSize">The maximum size of single batch of operations to be executed over a single <see cref="DbConnection"/>.</param>
        /// <param name="maxBufferSize">The maximum size of requests stored in journal buffer.</param>
        /// <param name="autoInitialize">
        /// If set to <c>true</c>, the journal executes all SQL scripts stored under the
        /// <see cref="BatchingSqlJournal{TConnection,TCommand}.Initializers"/> collection prior
        /// to starting executing any requests.
        /// </param>
        /// <param name="connectionTimeout">The maximum time given for executed <see cref="DbCommand"/> to complete.</param>
        /// <param name="isolationLevel">The isolation level of transactions used during read AND write query execution.</param>
        /// <param name="circuitBreakerSettings">
        /// The settings used by the <see cref="CircuitBreaker"/> when for executing request batches.
        /// </param>
        /// <param name="replayFilterSettings">The settings used when replaying events from database back to the persistent actors.</param>
        /// <param name="namingConventions">The naming conventions used by the database to construct valid SQL statements.</param>
        /// <param name="defaultSerializer">The serializer used when no specific type matching can be found.</param>
        [Obsolete("Use the constructor with read and write IsolationLevel arguments (since v1.5.2)")]
        public BatchingSqliteJournalSetup(
            string connectionString,
            int maxConcurrentOperations,
            int maxBatchSize,
            int maxBufferSize,
            bool autoInitialize, 
            TimeSpan connectionTimeout,
            IsolationLevel isolationLevel,
            CircuitBreakerSettings circuitBreakerSettings,
            ReplayFilterSettings replayFilterSettings,
            QueryConfiguration namingConventions,
            string defaultSerializer) 
            : base(
                connectionString: connectionString,
                maxConcurrentOperations: maxConcurrentOperations,
                maxBatchSize: maxBatchSize,
                maxBufferSize: maxBufferSize,
                autoInitialize: autoInitialize,
                connectionTimeout: connectionTimeout,
                readIsolationLevel: isolationLevel,
                writeIsolationLevel: isolationLevel,
                circuitBreakerSettings: circuitBreakerSettings,
                replayFilterSettings: replayFilterSettings,
                namingConventions: namingConventions,
                defaultSerializer: defaultSerializer)
        {
        }
        
        /// <summary>
        /// Initializes a new instance of the <see cref="BatchingSqliteJournalSetup" /> class.
        /// </summary>
        /// <param name="connectionString">The connection string used to connect to the database.</param>
        /// <param name="maxConcurrentOperations">The maximum number of batch operations allowed to be executed at the same time.</param>
        /// <param name="maxBatchSize">The maximum size of single batch of operations to be executed over a single <see cref="DbConnection"/>.</param>
        /// <param name="maxBufferSize">The maximum size of requests stored in journal buffer.</param>
        /// <param name="autoInitialize">
        /// If set to <c>true</c>, the journal executes all SQL scripts stored under the
        /// <see cref="BatchingSqlJournal{TConnection,TCommand}.Initializers"/> collection prior
        /// to starting executing any requests.
        /// </param>
        /// <param name="connectionTimeout">The maximum time given for executed <see cref="DbCommand"/> to complete.</param>
        /// <param name="readIsolationLevel">The isolation level of transactions used during read query execution.</param>
        /// <param name="writeIsolationLevel">The isolation level of transactions used during write query execution.</param>
        /// <param name="circuitBreakerSettings">
        /// The settings used by the <see cref="CircuitBreaker"/> when for executing request batches.
        /// </param>
        /// <param name="replayFilterSettings">The settings used when replaying events from database back to the persistent actors.</param>
        /// <param name="namingConventions">The naming conventions used by the database to construct valid SQL statements.</param>
        /// <param name="defaultSerializer">The serializer used when no specific type matching can be found.</param>
        public BatchingSqliteJournalSetup(
            string connectionString,
            int maxConcurrentOperations,
            int maxBatchSize,
            int maxBufferSize,
            bool autoInitialize, 
            TimeSpan connectionTimeout,
            IsolationLevel readIsolationLevel,
            IsolationLevel writeIsolationLevel,
            CircuitBreakerSettings circuitBreakerSettings,
            ReplayFilterSettings replayFilterSettings,
            QueryConfiguration namingConventions,
            string defaultSerializer) 
            : base(
                connectionString: connectionString,
                maxConcurrentOperations: maxConcurrentOperations,
                maxBatchSize: maxBatchSize,
                maxBufferSize: maxBufferSize,
                autoInitialize: autoInitialize,
                connectionTimeout: connectionTimeout,
                writeIsolationLevel: readIsolationLevel,
                readIsolationLevel: writeIsolationLevel,
                circuitBreakerSettings: circuitBreakerSettings,
                replayFilterSettings: replayFilterSettings,
                namingConventions: namingConventions,
                defaultSerializer: defaultSerializer)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class BatchingSqliteJournal : BatchingSqlJournal<SqliteConnection, SqliteCommand>
    {
        private DbConnection _anchor;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public BatchingSqliteJournal(Config config) : this(new BatchingSqliteJournalSetup(config))
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="setup">TBD</param>
        public BatchingSqliteJournal(BatchingSqliteJournalSetup setup) : base(setup)
        {
            var conventions = Setup.NamingConventions;
            Initializers = ImmutableDictionary.CreateRange(new[]
            {
                new KeyValuePair<string, string>("CreateJournalSql", $@"
                CREATE TABLE IF NOT EXISTS {conventions.FullJournalTableName} (
                    {conventions.OrderingColumnName} INTEGER PRIMARY KEY NOT NULL,
                    {conventions.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {conventions.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {conventions.IsDeletedColumnName} INTEGER(1) NOT NULL,
                    {conventions.ManifestColumnName} VARCHAR(255) NULL,
                    {conventions.TimestampColumnName} INTEGER NOT NULL,
                    {conventions.PayloadColumnName} BLOB NOT NULL,
                    {conventions.TagsColumnName} VARCHAR(2000) NULL,
                    {conventions.SerializerIdColumnName} INTEGER(4),
                    UNIQUE ({conventions.PersistenceIdColumnName}, {conventions.SequenceNrColumnName})
                );"),
                new KeyValuePair<string, string>("CreateMetadataSql", $@"
                CREATE TABLE IF NOT EXISTS {conventions.FullMetaTableName} (
                    {conventions.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {conventions.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    PRIMARY KEY ({conventions.PersistenceIdColumnName}, {conventions.SequenceNrColumnName})
                );"),
            });
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override ImmutableDictionary<string, string> Initializers { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            _anchor = CreateConnection(Setup.ConnectionString);
            _anchor.Open();
            base.PreStart();
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PostStop()
        {
            base.PostStop();
            _anchor.Dispose();
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected override SqliteConnection CreateConnection(string connectionString) => new SqliteConnection(connectionString);
    }
}
