using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Threading.Tasks;
using Akka.Persistence.Journal;
using Akka.Persistence.Sql.Common;
using Akka.Persistence.Sql.Common.Journal;

namespace Akka.Persistence.SqlServer.Journal
{
    /// <summary>
    /// Specialization of the <see cref="JournalDbEngine"/> which uses SQL Server as it's sql backend database.
    /// </summary>
    public class SqlJournalEngine : JournalDbEngine
    {
        public SqlJournalEngine(JournalSettings journalSettings, Akka.Serialization.Serialization serialization)
            : base(journalSettings, serialization)
        {
            QueryBuilder = new DefaultJournalQueryBuilder(journalSettings.TableName, journalSettings.SchemaName);
        }

        protected override DbConnection CreateDbConnection()
        {
            return new SqlConnection(Settings.ConnectionString);
        }

        protected override void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry)
        {
            sqlCommand.Parameters["@PersistenceId"].Value = entry.PersistenceId;
            sqlCommand.Parameters["@SequenceNr"].Value = entry.SequenceNr;
            sqlCommand.Parameters["@IsDeleted"].Value = entry.IsDeleted;
            sqlCommand.Parameters["@PayloadType"].Value = entry.PayloadType;
            sqlCommand.Parameters["@Payload"].Value = entry.Payload;
        }
    }

    /// <summary>
    /// Persistent journal actor using SQL Server as persistence layer. It processes write requests
    /// one by one in asynchronous manner, while reading results asynchronously.
    /// </summary>
    public class SqlServerJournal : AsyncWriteJournal
    {
        private readonly SqlServerPersistenceExtension _extension;

        private JournalDbEngine _engine;

        public SqlServerJournal()
        {
            _extension = SqlServerPersistence.Instance.Apply(Context.System);
        }

        /// <summary>
        /// Gets an engine instance responsible for handling all database-related journal requests.
        /// </summary>
        protected virtual JournalDbEngine Engine
        {
            get
            {
                return _engine ?? (_engine = new SqlJournalEngine(_extension.JournalSettings, Context.System.Serialization));
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

        protected override Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages)
        {
            return Engine.WriteMessagesAsync(messages);
        }

        protected override Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            return Engine.DeleteMessagesToAsync(persistenceId, toSequenceNr, isPermanent);
        }
    }

    /// <summary>
    /// Persistent journal actor using SQL Server as persistence layer. It processes write requests
    /// one by one in synchronous manner, while reading results asynchronously. Use for tests only.
    /// </summary>
    public class SyncSqlServerJournal : SyncWriteJournal
    {
        private readonly SqlServerPersistenceExtension _extension;
        private JournalDbEngine _engine;

        public SyncSqlServerJournal()
        {
            _extension = SqlServerPersistence.Instance.Apply(Context.System);
        }

        /// <summary>
        /// Gets an engine instance responsible for handling all database-related journal requests.
        /// </summary>
        protected virtual JournalDbEngine Engine
        {
            get
            {
                return _engine ?? (_engine = new SqlJournalEngine(_extension.JournalSettings, Context.System.Serialization));
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