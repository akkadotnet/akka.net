//-----------------------------------------------------------------------
// <copyright file="SqliteQueryExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;
using System.Data.SQLite;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    internal class SqliteQueryExecutor : AbstractQueryExecutor
    {
        public SqliteQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization, ITimestampProvider timestampProvider) 
            : base(configuration, serialization, timestampProvider)
        {
            InsertEventSql = base.InsertEventSql + @"; SELECT last_insert_rowid();";
            CreateEventsJournalSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullJournalTableName} (
                    id INTEGER NOT NULL PRIMARY KEY,
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    is_deleted INTEGER(1) NOT NULL,
                    manifest VARCHAR(255) NULL,
                    timestamp INTEGER NOT NULL,
                    payload BLOB NOT NULL,
                    UNIQUE (persistence_id, sequence_nr)
                );";
            CreateMetaTableSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullMetaTableName} (
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    PRIMARY KEY (persistence_id, sequence_nr)
                );";
        }

        protected override string InsertEventSql { get; }
        protected override string CreateEventsJournalSql { get; }
        protected override string CreateMetaTableSql { get; }

        protected override DbCommand CreateCommand(DbConnection connection)
        {
            return new SQLiteCommand((SQLiteConnection)connection);
        }
    }
}