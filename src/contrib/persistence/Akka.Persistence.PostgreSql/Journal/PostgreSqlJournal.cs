using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Journal;
using Npgsql;
using Akka.Persistence.Sql.Common.Journal;
using Akka.Persistence.Sql.Common;

namespace Akka.Persistence.PostgreSql.Journal
{
    public class PostgreSqlJournalEngine : JournalDbEngine
    {
        public PostgreSqlJournalEngine(JournalSettings journalSettings, Akka.Serialization.Serialization serialization)
            : base(journalSettings, serialization)
        {
            QueryBuilder = new PostgreSqlJournalQueryBuilder(journalSettings.TableName, journalSettings.SchemaName);
            QueryMapper = new PostgreSqlJournalQueryMapper(serialization);
        }

        protected override DbConnection CreateDbConnection()
        {
            return new NpgsqlConnection(Settings.ConnectionString);
        }

        protected override void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry)
        {
            sqlCommand.Parameters[":persistence_id"].Value = entry.PersistenceId;
            sqlCommand.Parameters[":sequence_nr"].Value = entry.SequenceNr;
            sqlCommand.Parameters[":is_deleted"].Value = entry.IsDeleted;
            sqlCommand.Parameters[":payload_type"].Value = entry.PayloadType;
            sqlCommand.Parameters[":payload"].Value = entry.Payload;
        }
    }

    /// <summary>
    /// Persistent journal actor using PostgreSQL as persistence layer. It processes write requests
    /// one by one in synchronous manner, while reading results asynchronously.
    /// </summary>
    public class PostgreSqlJournal : SyncWriteJournal
    {
        private readonly PostgreSqlPersistenceExtension _extension;
        private PostgreSqlJournalEngine _engine;

        public PostgreSqlJournal()
        {
            _extension = PostgreSqlPersistence.Instance.Apply(Context.System);
        }

        /// <summary>
        /// Gets an engine instance responsible for handling all database-related journal requests.
        /// </summary>
        protected virtual JournalDbEngine Engine
        {
            get
            {
                return _engine ?? (_engine = new PostgreSqlJournalEngine(_extension.JournalSettings, Context.System.Serialization));
            }
        }

        protected override void PreStart()
        {
            base.PreStart();
            Engine.Open();
        }

        protected override void PostStop()
        {
            base.PostStop();
            Engine.Close();
        }

        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            return Engine.ReplayMessagesAsync(persistenceId, fromSequenceNr, toSequenceNr, max, Context.Sender, replayCallback);
        }

        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            return Engine.ReadHighestSequenceNrAsync(persistenceId, fromSequenceNr);
        }

        public override void WriteMessages(IEnumerable<IPersistentRepresentation> messages)
        {
            Engine.WriteMessages(messages);
        }

        public override void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            Engine.DeleteMessagesTo(persistenceId, toSequenceNr, isPermanent);
        }
    }
}