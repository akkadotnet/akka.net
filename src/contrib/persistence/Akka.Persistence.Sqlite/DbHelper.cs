//-----------------------------------------------------------------------
// <copyright file="DbHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.SQLite;

namespace Akka.Persistence.Sqlite
{
    internal static class DbHelper
    {
        private const string JournalFormat = @"
                CREATE TABLE IF NOT EXISTS {0} (
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    is_deleted INTEGER(1) NOT NULL,
                    manifest VARCHAR(255) NOT NULL,
                    timestamp INTEGER NOT NULL,
                    payload BLOB NOT NULL,
                    PRIMARY KEY (persistence_id, sequence_nr)
                );";

        private const string SnapshotStoreFormat = @"
                CREATE TABLE IF NOT EXISTS {0} (
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    created_at INTEGER(8) NOT NULL,
                    manifest VARCHAR(255) NOT NULL,
                    snapshot BLOB NOT NULL,
                    PRIMARY KEY (persistence_id, sequence_nr)
                );";

        public static void CreateJournalTable(string connectionString, string tableName)
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException("connectionString", "SqlitePersistence requires connection string to be provided");
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentNullException("tableName", "SqlitePersistence requires journal table name to be provided");

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(string.Format(JournalFormat, tableName), connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public static void CreateSnapshotStoreTable(string connectionString, string tableName)
        {
            if (string.IsNullOrEmpty(connectionString)) throw new ArgumentNullException("connectionString", "SqlitePersistence requires connection string to be provided");
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentNullException("tableName", "SqlitePersistence requires snapshot store table name to be provided");

            using (var connection = new SQLiteConnection(connectionString))
            using (var command = new SQLiteCommand(string.Format(SnapshotStoreFormat, tableName), connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
        }
    }
}