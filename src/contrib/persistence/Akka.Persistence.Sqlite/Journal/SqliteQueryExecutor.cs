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
            ByTagSql = base.ByTagSql + " LIMIT @Take OFFSET @Skip";

            CreateEventsJournalSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullJournalTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {configuration.IsDeletedColumnName} INTEGER(1) NOT NULL,
                    {configuration.ManifestColumnName} VARCHAR(255) NULL,
                    {configuration.TimestampColumnName} INTEGER NOT NULL,
                    {configuration.PayloadColumnName} BLOB NOT NULL,
                    {configuration.TagsColumnName} VARCHAR(2000) NULL,
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";

            CreateMetaTableSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullMetaTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";
        }
        
        protected override string CreateEventsJournalSql { get; }
        protected override string CreateMetaTableSql { get; }

        protected override string ByTagSql { get; }

        protected override DbCommand CreateCommand(DbConnection connection)
        {
            return new SQLiteCommand((SQLiteConnection)connection);
        }
    }
}