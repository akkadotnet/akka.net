//-----------------------------------------------------------------------
// <copyright file="JournalDbEngine.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// Class used for storing intermediate result of the <see cref="IPersistentRepresentation"/>
    /// in form which is ready to be stored directly in the SQL table.
    /// </summary>
    public class JournalEntry
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

    /// <summary>
    /// Class used to abstract SQL persistence capabilities for concrete implementation of actor journal.
    /// </summary>
    /*TODO: this class is not used*/public abstract class JournalDbEngine : IDisposable
    {
        /// <summary>
        /// Settings applied to journal mapped from HOCON config file.
        /// </summary>
        public readonly JournalSettings Settings;

        /// <summary>
        /// List of cancellation tokens for each of the currently pending database operations.
        /// </summary>
        protected readonly LinkedList<CancellationTokenSource> PendingOperations;

        private readonly Akka.Serialization.Serialization _serialization;
        private DbConnection _dbConnection;

        protected JournalDbEngine(JournalSettings settings, Akka.Serialization.Serialization serialization)
        {
            Settings = settings;
            _serialization = serialization;

            QueryMapper = new DefaultJournalQueryMapper(serialization);

            PendingOperations = new LinkedList<CancellationTokenSource>();
        }

        /// <summary>
        /// Initializes a database connection.
        /// </summary>
        protected abstract DbConnection CreateDbConnection();

        /// <summary>
        /// Copies values from entities to database command.
        /// </summary>
        /// <param name="sqlCommand"></param>
        /// <param name="entry"></param>
        protected abstract void CopyParamsToCommand(DbCommand sqlCommand, JournalEntry entry);

        /// <summary>
        /// Gets database connection.
        /// </summary>
        public IDbConnection DbConnection { get { return _dbConnection; } }

        /// <summary>
        /// Used for generating SQL commands for journal-related database operations.
        /// </summary>
        public IJournalQueryBuilder QueryBuilder { get; protected set; }

        /// <summary>
        /// Used for mapping results returned from database into <see cref="IPersistentRepresentation"/> objects.
        /// </summary>
        public IJournalQueryMapper QueryMapper { get; protected set; }

        /// <summary>
        /// Initializes and opens a database connection.
        /// </summary>
        public void Open()
        {
            // close connection if it was open
            Close();

            _dbConnection = CreateDbConnection();
            _dbConnection.Open();
        }

        /// <summary>
        /// Closes database connection if exists.
        /// </summary>
        public void Close()
        {
            if (_dbConnection != null)
            {
                StopPendingOperations();

                _dbConnection.Dispose();
                _dbConnection = null;
            }
        }

        /// <summary>
        /// Stops all currently executing database operations.
        /// </summary>
        protected void StopPendingOperations()
        {
            // stop all operations executed in the background
            var node = PendingOperations.First;
            while (node != null)
            {
                var curr = node;
                node = node.Next;

                curr.Value.Cancel();
                PendingOperations.Remove(curr);
            }
        }

        void IDisposable.Dispose()
        {
            Close();
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
        public Task ReplayMessagesAsync(string persistenceId, long fromSequenceNr, long toSequenceNr, long max, IActorRef sender, Action<IPersistentRepresentation> replayCallback)
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
                            var persistent = QueryMapper.Map(reader, sender);
                            if (persistent != null)
                            {
                                replayCallback(persistent);
                            }
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
        public Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
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
        public void WriteMessages(IEnumerable<IPersistentRepresentation> messages)
        {
            var persistentMessages = messages.ToArray();
            var sqlCommand = QueryBuilder.InsertBatchMessages(persistentMessages);
            CompleteCommand(sqlCommand);

            var journalEntries = persistentMessages.Select(ToJournalEntry).ToList();

            InsertInTransaction(sqlCommand, journalEntries);
        }

        /// <summary>
        /// Synchronously deletes all persisted messages identified by provided <paramref name="persistenceId"/>
        /// up to provided message sequence number (inclusive). If <paramref name="isPermanent"/> flag is cleared,
        /// messages will still reside inside database, but will be logically counted as deleted.
        /// </summary>
        public void DeleteMessagesTo(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            var sqlCommand = QueryBuilder.DeleteBatchMessages(persistenceId, toSequenceNr, isPermanent);
            CompleteCommand(sqlCommand);

            sqlCommand.ExecuteNonQuery();
        }

        /// <summary>
        /// Asynchronously writes all persistent <paramref name="messages"/> inside SQL Server database.
        /// 
        /// Specific table used for message persistence may be defined through configuration within 
        /// 'akka.persistence.journal.sql-server' scope with 'schema-name' and 'table-name' keys.
        /// </summary>
        public async Task WriteMessagesAsync(IEnumerable<IPersistentRepresentation> messages)
        {
            var persistentMessages = messages.ToArray();
            var sqlCommand = QueryBuilder.InsertBatchMessages(persistentMessages);
            CompleteCommand(sqlCommand);

            var journalEntries = persistentMessages.Select(ToJournalEntry).ToList();

            await InsertInTransactionAsync(sqlCommand, journalEntries);
        }

        /// <summary>
        /// Asynchronously deletes all persisted messages identified by provided <paramref name="persistenceId"/>
        /// up to provided message sequence number (inclusive). If <paramref name="isPermanent"/> flag is cleared,
        /// messages will still reside inside database, but will be logically counted as deleted.
        /// </summary>
        public async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, bool isPermanent)
        {
            var sqlCommand = QueryBuilder.DeleteBatchMessages(persistenceId, toSequenceNr, isPermanent);
            CompleteCommand(sqlCommand);

            await sqlCommand.ExecuteNonQueryAsync();
        }

        private void CompleteCommand(DbCommand sqlCommand)
        {
            sqlCommand.Connection = _dbConnection;
            sqlCommand.CommandTimeout = (int)Settings.ConnectionTimeout.TotalMilliseconds;
        }

        private CancellationTokenSource GetCancellationTokenSource()
        {
            var source = new CancellationTokenSource();
            PendingOperations.AddLast(source);
            return source;
        }

        private JournalEntry ToJournalEntry(IPersistentRepresentation message)
        {
            var payloadType = message.Payload.GetType();
            var serializer = _serialization.FindSerializerForType(payloadType);

            return new JournalEntry(message.PersistenceId, message.SequenceNr, message.IsDeleted,
                payloadType.QualifiedTypeName(), serializer.ToBinary(message.Payload));
        }

        private void InsertInTransaction(DbCommand sqlCommand, IEnumerable<JournalEntry> journalEntries)
        {
            using (var tx = _dbConnection.BeginTransaction())
            {
                sqlCommand.Transaction = tx;
                try
                {
                    foreach (var entry in journalEntries)
                    {
                        CopyParamsToCommand(sqlCommand, entry);

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

        private async Task InsertInTransactionAsync(DbCommand sqlCommand, IEnumerable<JournalEntry> journalEntries)
        {
            using (var tx = _dbConnection.BeginTransaction())
            {
                sqlCommand.Transaction = tx;
                try
                {
                    foreach (var entry in journalEntries)
                    {
                        CopyParamsToCommand(sqlCommand, entry);

                        var commandResult = await sqlCommand.ExecuteNonQueryAsync();
                        if (commandResult != 1)
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