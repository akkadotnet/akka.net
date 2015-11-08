﻿using System.Data.Common;
using System.Data.SQLite;
using Akka.Actor;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.Sqlite.Journal
{
    public class SqliteJournalEngine : JournalDbEngine
    {
        public SqliteJournalEngine(ActorSystem system)
            : base(system)
        {
            QueryBuilder = new SqliteQueryBuilder(Settings.TableName);
        }

        protected override string JournalConfigPath { get { return SqliteJournalSettings.ConfigPath; } }

        protected override DbConnection CreateDbConnection(string connectionString)
        {
            return new SQLiteConnection(connectionString);
        }

        protected override void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry)
        {
            sqlCommand.Parameters["@PersistenceId"].Value = entry.PersistenceId;
            sqlCommand.Parameters["@SequenceNr"].Value = entry.SequenceNr;
            sqlCommand.Parameters["@IsDeleted"].Value = entry.IsDeleted;
            sqlCommand.Parameters["@Manifest"].Value = entry.Manifest;
            sqlCommand.Parameters["@Timestamp"].Value = entry.Timestamp;
            sqlCommand.Parameters["@Payload"].Value = entry.Payload;
        }
    }

    public class SqliteJournal : SqlJournal
    {
        public readonly SqlitePersistence Extension = SqlitePersistence.Get(Context.System);
        public SqliteJournal() : base(new SqliteJournalEngine(Context.System))
        {
        }
    }
}