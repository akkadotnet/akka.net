using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Persistence.Journal;

namespace Akka.Persistence.SqlServer.Journal
{
    /// <summary>
    /// Persistent journal actor using SQL Server as persistence layer. It processes write requests
    /// one by one in synchronous manner, while reading results asynchronously.
    /// </summary>
    public class SqlServerJournal : SyncWriteJournal
    {
        #region journal internal types definitions

        internal class JournalEntry
        {
            public readonly string PersistenceId;
            public readonly long SequenceNr;
            public readonly bool IsDeleted;
            public readonly string PayloadType;
            public readonly byte[] Payload;

            public JournalEntry(string persistenceId, long sequenceNr, bool isDeleted, string payloadType, byte[] payload)
            {
                PersistenceId = persistenceId;
                SequenceNr = sequenceNr;
                IsDeleted = isDeleted;
                PayloadType = payloadType;
                Payload = payload;
            }
        }

        #endregion

        private readonly SqlServerPersistenceExtension _extension;
        private SqlConnection _connection;

        protected readonly LinkedList<CancellationTokenSource> PendingOperations;

        /// <summary>
        /// Used for generating SQL commands for journal-related database operations.
        /// </summary>
        public IJournalQueryBuilder QueryBuilder { get; protected set; }

        /// <summary>
        /// Used for mapping results returned from database into <see cref="IPersistentRepresentation"/> objects.
        /// </summary>
        public IJournalQueryMapper QueryMapper { get; protected set; }

        public SqlServerJournal()
        {
            _extension = SqlServerPersistence.Instance.Apply(Context.System);

            var settings = _extension.JournalSettings;
            QueryBuilder = new DefaultJournalQueryBuilder(settings.TableName, settings.SchemaName);
            QueryMapper = new DefaultJournalQueryMapper(Context.System.Serialization);
            PendingOperations = new LinkedList<CancellationTokenSource>();
        }

        protected override void PreStart()
        {
            base.PreStart();

            _connection = new SqlConnection(_extension.JournalSettings.ConnectionString);
            _connection.Open();
        }

        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            var node = PendingOperations.First;
            while (node != null)
            {
                var curr = node;
                node = node.Next;

                curr.Value.Cancel();
                PendingOperations.Remove(curr);
            }

            _connection.Close();
        }

        /// <summary>
        /// Asynchronously replays all requested messages related to provided <paramref name="persistenceId"/>,
        /// using provided sequence ranges (inclusive) with <paramref name="max"/> number of messages replayed
        /// (counting from the beginning). Replay callback is invoked for each replayed message.
        /// </summary>
        /// <param name="persistenceId">Identifier of persistent messages stream to be replayed.</param>
        /// <param name="fromSequenceNr">Lower inclusive sequence number bound. Unbound by default.</param>
        /// <param name="toSequenceNr">Upper inclusive sequence number bound. Unbound by default.</param>
        /// <param name="max">Maximum number of messages to be replayed. Unbound by default.</param>
        /// <param name="replayCallback">Action invoked for each replayed message.</param>
        public override Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> replayCallback)
        {
            var sqlCommand = QueryBuilder.SelectMessages(persistenceId, fromSequenceNr, toSequenceNr, max);
            CompleteCommand(sqlCommand);

            var tokenSource = GetCancellationTokenSource();

            return sqlCommand
                .ExecuteReaderAsync(tokenSource.Token)
                .ContinueWith(task =>
                {
                    var reader = task.Result;
                    try
                    {
                        while (reader.Read())
                        {
                            var persistent = QueryMapper.Map(reader);
                            if (persistent != null)
                                replayCallback(persistent);
                        }
                    }
                    finally
                    {
                        PendingOperations.Remove(tokenSource);
                        reader.Close();
                    }
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        /// <summary>
        /// Asynchronously reads a highest sequence number of the event stream related with provided <paramref name="persistenceId"/>.
        /// </summary>
        public override Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            var sqlCommand = QueryBuilder.SelectHighestSequenceNr(persistenceId);
            CompleteCommand(sqlCommand);

            var tokenSource = GetCancellationTokenSource();

            return sqlCommand
                .ExecuteScalarAsync(tokenSource.Token)
                .ContinueWith(task =>
                {
                    PendingOperations.Remove(tokenSource);
                    var result = task.Result;
                    return result is long ? Convert.ToInt64(task.Result) : 0L;
                }, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.AttachedToParent);
        }

        /// <summary>
        /// Synchronously writes all persistent <paramref name="messages"/> inside SQL Server database.
        /// 
        /// Specific table used for message persistence may be defined through configuration within 
        /// 'akka.persistence.journal.sql-server' scope with 'schema-name' and 'table-name' keys.
        /// </summary>
        public override void WriteMessages(IEnumerable<IPersistentRepresentation> messages)
        {
            var persistentMessages = messages.ToArray();
            var sqlCommand = QueryBuilder.InsertBatchMessages(persistentMessages);
            CompleteCommand(sqlCommand);

            var journalEntires = persistentMessages.Select(ToJournalEntry).ToList();

            InsertInTransaction(sqlCommand, journalEntires);
        }

        /// <summary>
        /// Synchronously deletes all persisted messages identified by provided <paramref name="persistenceId"/>
        /// up to provided message sequence number (inclusive). If <paramref name="isPermanent"/> flag is cleared,
        /// messages will still reside inside database, but will be logically counted as deleted.
        /// </summary>
        public override void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            var sqlCommand = QueryBuilder.DeleteBatchMessages(persistenceId, toSequenceNr, isPermanent);
            CompleteCommand(sqlCommand);

            sqlCommand.ExecuteNonQuery();
        }

        private void CompleteCommand(SqlCommand sqlCommand)
        {
            sqlCommand.Connection = _connection;
            sqlCommand.CommandTimeout = (int)_extension.JournalSettings.ConnectionTimeout.TotalMilliseconds;
        }

        private CancellationTokenSource GetCancellationTokenSource()
        {
            var source = new CancellationTokenSource();
            PendingOperations.AddLast(source);
            return source;
        }

        private static JournalEntry ToJournalEntry(IPersistentRepresentation message)
        {
            var payloadType = message.Payload.GetType();
            var serializer = Context.System.Serialization.FindSerializerForType(payloadType);

            return new JournalEntry(message.PersistenceId, message.SequenceNr, message.IsDeleted,
                payloadType.QualifiedTypeName(), serializer.ToBinary(message.Payload));
        }

        private void InsertInTransaction(SqlCommand sqlCommand, IEnumerable<JournalEntry> journalEntires)
        {
            using (var tx = _connection.BeginTransaction())
            {
                sqlCommand.Transaction = tx;
                try
                {
                    foreach (var entry in journalEntires)
                    {
                        sqlCommand.Parameters["@PersistenceId"].Value = entry.PersistenceId;
                        sqlCommand.Parameters["@SequenceNr"].Value = entry.SequenceNr;
                        sqlCommand.Parameters["@IsDeleted"].Value = entry.IsDeleted;
                        sqlCommand.Parameters["@PayloadType"].Value = entry.PayloadType;
                        sqlCommand.Parameters["@Payload"].Value = entry.Payload;

                        if (sqlCommand.ExecuteNonQuery() != 1)
                        {
                            //TODO: something went wrong, ExecuteNonQuery() should return 1 (number of rows added)
                        }
                    }

                    tx.Commit();
                }
                catch (Exception)
                {
                    tx.Rollback();
                    throw;
                }
            }
        }
    }
}