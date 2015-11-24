//-----------------------------------------------------------------------
// <copyright file="DbHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data.Common;

namespace Akka.Persistence.Sql.Common
{
    internal static class CommonDbHelper
    {
        private const string CreateJournalFormat = @"
                IF OBJECT_ID('{0}.{1}', 'U') IS NULL 
                    CREATE TABLE {0}.{1} (
                        persistence_id VARCHAR(255) NOT NULL,
                        sequence_nr bigint NOT NULL,
                        is_deleted bit NOT NULL,
                        manifest VARCHAR(255) NOT NULL,
                        timestamp datetime2 NOT NULL,
                        payload varbinary(max) NOT NULL,
                        PRIMARY KEY (persistence_id, sequence_nr)
                    );";
        
        private const string CreateSnapshotStoreFormat = @"
                IF OBJECT_ID('{0}.{1}', 'U') IS NULL 
                    CREATE TABLE {0}.{1} (
                        persistence_id VARCHAR(255) NOT NULL,
                        sequence_nr bigint NOT NULL,
                        created_at bigint NOT NULL,
                        manifest VARCHAR(255) NOT NULL,
                        snapshot varbinary(max) NOT NULL,
                        PRIMARY KEY (persistence_id, sequence_nr)
                    );";

        private const string DropTableFormat = @"
                IF OBJECT_ID('{0}.{1}', 'U') IS NOT NULL 
                    DROP TABLE {0}.{1};";

        public static void CreateJournalTable(DbConnection connection, string tableName, string schema = "dbo")
        {
            ExecuteNonQuery(connection, CreateJournalFormat, tableName, schema);
        }

        public static void CreateSnapshotStoreTable(DbConnection connection, string tableName, string schema = "dbo")
        {
            ExecuteNonQuery(connection, CreateSnapshotStoreFormat, tableName, schema);
        }

        public static void DropTable(DbConnection connection, string tableName, string schema = "dbo")
        {
            ExecuteNonQuery(connection, DropTableFormat, tableName, schema);
        }

        private static int ExecuteNonQuery(DbConnection connection, String commandFormat, string tableName, string schema = "dbo")
        {
            if (connection == null)
                throw new ArgumentNullException("connection", "CommonPersistence requires connection to be provided");
            if (string.IsNullOrEmpty(commandFormat))
                throw new ArgumentNullException("commandFormat", "CommonPersistence requires command to be provided");
            if (string.IsNullOrEmpty(tableName)) 
                throw new ArgumentNullException("tableName", "CommonPersistence requires journal table name to be provided");

            using (var command = connection.CreateCommand())
            {
                command.CommandText = string.Format(commandFormat, schema, tableName);

                if (connection.State != System.Data.ConnectionState.Open)
                    connection.Open();

                return command.ExecuteNonQuery();
            }
        }
    }
}