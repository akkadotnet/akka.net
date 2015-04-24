using System;
using System.Data.SqlClient;

namespace Akka.Persistence.SqlServer
{
    internal static class SqlServerInitializer
    {
        private const string SqlJournalFormat = @"
            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{2}' AND TABLE_NAME = '{3}')
            BEGIN
                CREATE TABLE {0}.{1} (
	                PersistenceID NVARCHAR(200) NOT NULL,
                    CS_PID AS CHECKSUM(PersistenceID),
	                SequenceNr BIGINT NOT NULL,
	                IsDeleted BIT NOT NULL,
                    PayloadType NVARCHAR(500) NOT NULL,
	                Payload VARBINARY(MAX) NOT NULL
                    CONSTRAINT PK_{3} PRIMARY KEY (PersistenceID, SequenceNr)
                );
                CREATE INDEX IX_{3}_CS_PID ON {0}.{1}(CS_PID);
                CREATE INDEX IX_{3}_SequenceNr ON {0}.{1}(SequenceNr);
            END
            ";

        private const string SqlSnapshotStoreFormat = @"
            IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = '{2}' AND TABLE_NAME = '{3}')
            BEGIN
                CREATE TABLE {0}.{1} (
	                PersistenceID NVARCHAR(200) NOT NULL,
                    CS_PID AS CHECKSUM(PersistenceID),
	                SequenceNr BIGINT NOT NULL,
                    Timestamp DATETIME2 NOT NULL,
                    SnapshotType NVARCHAR(500) NOT NULL,
	                Snapshot VARBINARY(MAX) NOT NULL
                    CONSTRAINT PK_{3} PRIMARY KEY (PersistenceID, SequenceNr)
                );
                CREATE INDEX IX_{3}_CS_PID ON {0}.{1}(CS_PID);
                CREATE INDEX IX_{3}_SequenceNr ON {0}.{1}(SequenceNr);
                CREATE INDEX IX_{3}_Timestamp ON {0}.{1}(Timestamp);
            END
            ";

        /// <summary>
        /// Initializes a SQL Server journal-related tables according to 'schema-name', 'table-name' 
        /// and 'connection-string' values provided in 'akka.persistence.journal.sql-server' config.
        /// </summary>
        internal static void CreateSqlServerJournalTables(string connectionString, string schemaName, string tableName)
        {
            var sql = InitJournalSql(tableName, schemaName);
            ExecuteSql(connectionString, sql);
        }

        /// <summary>
        /// Initializes a SQL Server snapshot store related tables according to 'schema-name', 'table-name' 
        /// and 'connection-string' values provided in 'akka.persistence.snapshot-store.sql-server' config.
        /// </summary>
        internal static void CreateSqlServerSnapshotStoreTables(string connectionString, string schemaName, string tableName)
        {
            var sql = InitSnapshotStoreSql(tableName, schemaName);
            ExecuteSql(connectionString, sql);
        }

        private static string InitJournalSql(string tableName, string schemaName = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentNullException("tableName", "Akka.Persistence.SqlServer journal table name is required");
            schemaName = schemaName ?? "dbo";

            var cb = new SqlCommandBuilder();
            return string.Format(SqlJournalFormat, cb.QuoteIdentifier(schemaName), cb.QuoteIdentifier(tableName), cb.UnquoteIdentifier(schemaName), cb.UnquoteIdentifier(tableName));
        }

        private static string InitSnapshotStoreSql(string tableName, string schemaName = null)
        {
            if (string.IsNullOrEmpty(tableName)) throw new ArgumentNullException("tableName", "Akka.Persistence.SqlServer snapshot store table name is required");
            schemaName = schemaName ?? "dbo";

            var cb = new SqlCommandBuilder();
            return string.Format(SqlSnapshotStoreFormat, cb.QuoteIdentifier(schemaName), cb.QuoteIdentifier(tableName), cb.UnquoteIdentifier(schemaName), cb.UnquoteIdentifier(tableName));
        }

        private static void ExecuteSql(string connectionString, string sql)
        {
            using (var conn = new SqlConnection(connectionString))
            using (var command = conn.CreateCommand())
            {
                conn.Open();

                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }
}