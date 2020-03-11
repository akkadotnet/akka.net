//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
                    journalEventsTableName: config.GetString("table-name", null),
                    metaTableName: config.GetString("metadata-table-name", null),
                    persistenceIdColumnName: "persistence_id",
                    sequenceNrColumnName: "sequence_nr",
                    payloadColumnName: "payload",
                    manifestColumnName: "manifest",
                    timestampColumnName: "timestamp",
                    isDeletedColumnName: "is_deleted",
                    tagsColumnName: "tags",
                    orderingColumnName: "ordering",
                    serializerIdColumnName: "serializer_id",
                    timeout: config.GetTimeSpan("connection-timeout", null),
                    defaultSerializer: config.GetString("serializer", null),
                    useSequentialAccess: config.GetBoolean("use-sequential-access", false)))
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
        /// <param name="isolationLevel">The isolation level of transactions used during query execution.</param>
        /// <param name="circuitBreakerSettings">
        /// The settings used by the <see cref="CircuitBreaker"/> when for executing request batches.
        /// </param>
        /// <param name="replayFilterSettings">The settings used when replaying events from database back to the persistent actors.</param>
        /// <param name="namingConventions">The naming conventions used by the database to construct valid SQL statements.</param>
        /// <param name="defaultSerializer">The serializer used when no specific type matching can be found.</param>
        public BatchingSqliteJournalSetup(string connectionString, int maxConcurrentOperations, int maxBatchSize, int maxBufferSize, bool autoInitialize, 
            TimeSpan connectionTimeout, IsolationLevel isolationLevel, CircuitBreakerSettings circuitBreakerSettings, ReplayFilterSettings replayFilterSettings, QueryConfiguration namingConventions, string defaultSerializer) 
            : base(connectionString, maxConcurrentOperations, maxBatchSize, maxBufferSize, autoInitialize, connectionTimeout, isolationLevel, circuitBreakerSettings, replayFilterSettings, namingConventions, defaultSerializer)
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
