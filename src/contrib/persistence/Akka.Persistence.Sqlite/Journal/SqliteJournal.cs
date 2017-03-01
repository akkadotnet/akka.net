//-----------------------------------------------------------------------
// <copyright file="SqliteJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;
using System.Data.SQLite;
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
                timeout: config.GetTimeSpan("connection-timeout")), 
                    Context.System.Serialization, 
                    GetTimestampProvider(config.GetString("timestamp-provider")));
        }

        /// <summary>
        /// TBD
        /// </summary>
        public override IJournalQueryExecutor QueryExecutor { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected override string JournalConfigPath => SqliteJournalSettings.ConfigPath;
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connectionString">TBD</param>
        /// <returns>TBD</returns>
        protected override DbConnection CreateDbConnection(string connectionString)
        {
            return new SQLiteConnection(connectionString);
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override void PreStart()
        {
            try
            {
                ConnectionContext.Remember(GetConnectionString());
                base.PreStart();
            }
            catch (Exception e)
            {
                Log.Error(e, "Failure creating DbConnection");
                throw;
            }
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