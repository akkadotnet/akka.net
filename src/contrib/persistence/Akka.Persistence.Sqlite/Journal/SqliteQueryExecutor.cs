//-----------------------------------------------------------------------
// <copyright file="SqliteQueryExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    /// <summary>
    /// TBD
    /// </summary>
    internal class SqliteQueryExecutor : AbstractQueryExecutor
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configuration">TBD</param>
        /// <param name="serialization">TBD</param>
        /// <param name="timestampProvider">TBD</param>
        public SqliteQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization, ITimestampProvider timestampProvider) 
            : base(configuration, serialization, timestampProvider)
        {
            ByTagSql = base.ByTagSql + " LIMIT @Take";
            AllEventsSql = base.AllEventsSql + " LIMIT @Take";

            CreateEventsJournalSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullJournalTableName} (
                    {configuration.OrderingColumnName} INTEGER PRIMARY KEY NOT NULL,
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    {configuration.IsDeletedColumnName} INTEGER(1) NOT NULL,
                    {configuration.ManifestColumnName} VARCHAR(255) NULL,
                    {configuration.TimestampColumnName} INTEGER NOT NULL,
                    {configuration.PayloadColumnName} BLOB NOT NULL,
                    {configuration.TagsColumnName} VARCHAR(2000) NULL,
                    {configuration.SerializerIdColumnName} INTEGER(4),
                    UNIQUE ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";

            CreateMetaTableSql = $@"
                CREATE TABLE IF NOT EXISTS {configuration.FullMetaTableName} (
                    {configuration.PersistenceIdColumnName} VARCHAR(255) NOT NULL,
                    {configuration.SequenceNrColumnName} INTEGER(8) NOT NULL,
                    PRIMARY KEY ({configuration.PersistenceIdColumnName}, {configuration.SequenceNrColumnName})
                );";
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string CreateEventsJournalSql { get; }
        
        /// <summary>
        /// TBD
        /// </summary>
        protected override string CreateMetaTableSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        protected override string ByTagSql { get; }

        protected override string AllEventsSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <returns>TBD</returns>
        protected override DbCommand CreateCommand(DbConnection connection)
        {
            return new SqliteCommand { Connection = (SqliteConnection)connection };
        }
    }
}
