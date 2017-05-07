//-----------------------------------------------------------------------
// <copyright file="BatchingSqliteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akka.Configuration;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public sealed class BatchingSqliteJournalSetup : BatchingSqlJournalSetup
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
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
                    timeout: config.GetTimeSpan("connection-timeout")))
        {
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <param name="maxConcurrentOperations">TBD</param>
        /// <param name="maxBatchSize">TBD</param>
        /// <param name="maxBufferSize">TBD</param>
        /// <param name="autoInitialize">TBD</param>
        /// <param name="connectionTimeout">TBD</param>
        /// <param name="isolationLevel">TBD</param>
        /// <param name="circuitBreakerSettings">TBD</param>
        /// <param name="replayFilterSettings">TBD</param>
        /// <param name="namingConventions">TBD</param>
        public BatchingSqliteJournalSetup(string connectionString, int maxConcurrentOperations, int maxBatchSize, int maxBufferSize, bool autoInitialize, 
            TimeSpan connectionTimeout, IsolationLevel isolationLevel, CircuitBreakerSettings circuitBreakerSettings, ReplayFilterSettings replayFilterSettings, QueryConfiguration namingConventions) 
            : base(connectionString, maxConcurrentOperations, maxBatchSize, maxBufferSize, autoInitialize, connectionTimeout, isolationLevel, circuitBreakerSettings, replayFilterSettings, namingConventions)
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