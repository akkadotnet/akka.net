//-----------------------------------------------------------------------
// <copyright file="SqliteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akka.Configuration;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SqliteJournal : SqlJournal
    {
        /// <summary>
        /// TBD
        /// </summary>
        public static readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="journalConfig">TBD</param>
        public SqliteJournal(Config journalConfig) : base(journalConfig.WithFallback(Extension.DefaultJournalConfig))
        {
            var config = journalConfig.WithFallback(Extension.DefaultJournalConfig);
            QueryExecutor = new SqliteQueryExecutor(new QueryConfiguration(
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
                useSequentialAccess: config.GetBoolean("use-sequential-access", false)), 
                    Context.System.Serialization, 
                    GetTimestampProvider(config.GetString("timestamp-provider", null)));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IJournalQueryExecutor QueryExecutor { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        protected override string JournalConfigPath => SqlitePersistence.JournalConfigPath;

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
