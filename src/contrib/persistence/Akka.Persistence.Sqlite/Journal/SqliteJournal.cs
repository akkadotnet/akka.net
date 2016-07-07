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
    public class SqliteJournal : SqlJournal
    {
        public static readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);
        public SqliteJournal(Config journalConfig) : base(journalConfig.WithFallback(Extension.DefaultJournalConfig))
        {
            var config = journalConfig.WithFallback(Extension.DefaultJournalConfig);
            QueryExecutor = new SqliteQueryExecutor(new QueryConfiguration(
                schemaName: null,
                journalEventsTableName: config.GetString("table-name"),
                metaTableName: config.GetString("meta-table-name"),
                persistenceIdColumnName: "persistence_id",
                sequenceNrColumnName: "sequence_nr",
                payloadColumnName: "payload",
                manifestColumnName: "manifest",
                timestampColumnName: "timestamp",
                isDeletedColumnName: "is_deleted",
                tagsColumnName: "tags",
                timeout: config.GetTimeSpan("connection-timeout")), 
                    Context.System.Serialization, 
                    GetTimestampProvider(config.GetString("timestamp-provider")));
        }

        public override IJournalQueryExecutor QueryExecutor { get; }
        protected override string JournalConfigPath => SqliteJournalSettings.ConfigPath;
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