// //-----------------------------------------------------------------------
// // <copyright file="SqliteJournal.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.Util;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Custom.Journal
{
    public class SqliteJournal: AsyncWriteJournal, IWithUnboundedStash
    {
        // <schema>
        private const string CreateEventsJournalSql = @"
            CREATE TABLE IF NOT EXISTS event_journal (
                ordering INTEGER PRIMARY KEY NOT NULL,
                persistence_id VARCHAR(255) NOT NULL,
                sequence_nr INTEGER(8) NOT NULL,
                is_deleted INTEGER(1) NOT NULL,
                manifest VARCHAR(255) NULL,
                timestamp INTEGER NOT NULL,
                payload BLOB NOT NULL,
                serializer_id INTEGER(4),
                UNIQUE (persistence_id, sequence_nr));";

        private const string CreateMetaTableSql = @"
            CREATE TABLE IF NOT EXISTS journal_metadata (
                persistence_id VARCHAR(255) NOT NULL,
                sequence_nr INTEGER(8) NOT NULL,
                PRIMARY KEY (persistence_id, sequence_nr));";
        // </schema>
        
        // <ByPersistenceIdSql>
        private const string ByPersistenceIdSql = @"
            SELECT e.persistence_id as PersistenceId,
                    e.sequence_nr as SequenceNr,
                    e.timestamp as Timestamp,
                    e.is_deleted as IsDeleted,
                    e.manifest as Manifest,
                    e.payload as Payload,
                    e.serializer_id as SerializerId
                FROM event_journal e
                WHERE e.persistence_id = @PersistenceId
                AND e.sequence_nr BETWEEN @FromSequenceNr AND @ToSequenceNr
                ORDER BY sequence_nr ASC;";
        // </ByPersistenceIdSql>

        // <HighestSequenceNrSql>
        private const string HighestSequenceNrSql = @"
            SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT MAX(e.sequence_nr) as SeqNr FROM event_journal e 
                        WHERE e.persistence_id = @PersistenceId
                    UNION
                    SELECT MAX(m.sequence_nr) as SeqNr FROM journal_metadata m 
                        WHERE m.persistence_id = @PersistenceId) as u";
        // </HighestSequenceNrSql>

        // <InsertEventSql>
        private const string InsertEventSql = @"
            INSERT INTO event_journal (
                persistence_id,
                sequence_nr,
                timestamp,
                is_deleted,
                manifest,
                payload,
                serializer_id)
            VALUES (
                @PersistenceId,
                @SequenceNr,
                @Timestamp,
                @IsDeleted,
                @Manifest,
                @Payload,
                @SerializerId);";
        // </InsertEventSql>

        // <DeleteBatchSql>
        private const string DeleteBatchSql = @"
            DELETE FROM event_journal
                WHERE persistence_id = @PersistenceId AND sequence_nr <= @ToSequenceNr;
            DELETE FROM journal_metadata
                WHERE persistence_id = @PersistenceId AND sequence_nr <= @ToSequenceNr;";
        // </DeleteBatchSql>

        // <UpdateSequenceNrSql>
        private const string UpdateSequenceNrSql = @"
            INSERT INTO journal_metadata (persistence_id, sequence_nr)
            VALUES (@PersistenceId, @SequenceNr);";
        // </UpdateSequenceNrSql>

        private readonly JournalSettings _settings;
        private readonly string _connectionString;
        private readonly TimeSpan _timeout;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly string _defaultSerializer;
        private readonly ILoggingAdapter _log;
        private readonly CancellationTokenSource _pendingRequestsCancellation;

        public SqliteJournal()
        {
            _settings = new JournalSettings(SqlitePersistence.Get(Context.System).JournalConfig);
            
            _connectionString = _settings.ConnectionString;
            _timeout = _settings.ConnectionTimeout;
            _defaultSerializer = _settings.DefaultSerializer;
            
            _serialization = Context.System.Serialization;
            _log = Context.GetLogger();
            _pendingRequestsCancellation = new CancellationTokenSource();
        }

        public IStash Stash { get; set; }
        
        //<Startup>
        protected override void PreStart()
        {
            base.PreStart();
            
            // Call the Initialize method and pipe the result back to signal that
            // database schemas are ready to use, if it needs to be initialized
            Initialize().PipeTo(Self);
            
            // WaitingForInitialization receive handler will wait for a success/fail
            // result back from the Initialize method
            BecomeStacked(WaitingForInitialization);
        }

        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            _pendingRequestsCancellation.Cancel();
        }

        private bool WaitingForInitialization(object message)
        {
            switch (message)
            {
                // Tables are already created or successfully created all needed tables
                case Status.Success _:
                    UnbecomeStacked();
                    // Unstash all messages received when we were initializing our tables
                    Stash.UnstashAll();
                    break;
                
                case Status.Failure fail:
                    // Failed creating tables. Log an error and stop the actor.
                    _log.Error(fail.Cause, "Failure during {0} initialization.", Self);
                    Context.Stop(Self);
                    break;
                
                default:
                    // By default, stash all received messages while we're waiting for the
                    // Initialize method.
                    Stash.Stash();
                    break;
            }
            return true;
        }
        
        private async Task<object> Initialize()
        {
            // No database initialization needed, the user explicitly asked us
            // not to initialize anything.
            if (!_settings.AutoInitialize) 
                return new Status.Success(NotUsed.Instance);

            // Create SQLite journal tables 
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                using (var cts = CancellationTokenSource
                           .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    await connection.OpenAsync(cts.Token);
                    using (var command = GetCommand(connection, CreateEventsJournalSql, _timeout))
                    {
                        await command.ExecuteNonQueryAsync(cts.Token);
                        command.CommandText = CreateMetaTableSql;
                        await command.ExecuteNonQueryAsync(cts.Token);
                    }
                }
            }
            catch (Exception e)
            {
                return new Status.Failure(e);
            }
            
            return new Status.Success(NotUsed.Instance);
        }
        //</Startup>
        
        // <ReplayMessagesAsync>
        public sealed override async Task ReplayMessagesAsync(
            IActorContext context,
            string persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                
                // Create new DbCommand instance
                using (var command = GetCommand(connection, ByPersistenceIdSql, _timeout))
                {
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@FromSequenceNr", DbType.Int64, fromSequenceNr);
                    AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);
                    
                    // Create a DbDataReader to sequentially read the returned query result
                    using (var reader = await command.ExecuteReaderAsync(
                               behavior: CommandBehavior.SequentialAccess, 
                               cancellationToken: cts.Token))
                    {
                        var i = 0L;
                        while (i++ < max && await reader.ReadAsync(cts.Token))
                        {
                            var id = reader.GetString(0);
                            var sequenceNr = reader.GetInt64(1);
                            var timestamp = reader.GetInt64(2);
                            var isDeleted = reader.GetBoolean(3);
                            var manifest = reader.GetString(4);
                            var payload = reader[5];
                            var serializerId = reader.GetInt32(6);
                            
                            // Deserialize the persistent payload using the data we read from the reader
                            var deserialized = _serialization.Deserialize(
                                bytes: (byte[])payload, 
                                serializerId: serializerId, 
                                manifest: manifest);
                            
                            // Call the recovery callback with the deserialized data from the database
                            recoveryCallback(new Persistent(
                                payload: deserialized, 
                                sequenceNr: sequenceNr,
                                persistenceId: id,
                                manifest: manifest, 
                                isDeleted: isDeleted, 
                                sender: ActorRefs.NoSender, 
                                writerGuid: null, 
                                timestamp: timestamp));
                        }
                        command.Cancel();
                    }
                }
            }
        }
        // </ReplayMessagesAsync>

        // <ReadHighestSequenceNrAsync>
        public sealed override async Task<long> ReadHighestSequenceNrAsync(
            string persistenceId,
            long fromSequenceNr)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                // Create new DbCommand instance
                using (var command = GetCommand(connection, HighestSequenceNrSql, _timeout))
                {
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
        
                    // Execute the SQL query statement
                    var result = await command.ExecuteScalarAsync(cts.Token);
        
                    // Return the result if one is returned by the query result, else return zero
                    return result is long ? Convert.ToInt64(result) : 0L;
                }
            }
        }
        // </ReadHighestSequenceNrAsync>

        // <WriteMessagesAsync>
        protected sealed override async Task<IImmutableList<Exception>> WriteMessagesAsync(
            IEnumerable<AtomicWrite> messages)
        {
            // For each of the atomic write request, create an async Task 
            var writeTasks = messages.Select(async message =>
            {
                // Create a new DbConnection instance
                using (var connection = new SqliteConnection(_connectionString))
                using (var cts = CancellationTokenSource
                           .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    await connection.OpenAsync(cts.Token);
                    
                    // Create new DbCommand instance
                    using (var command = GetCommand(connection, InsertEventSql, _timeout))
                    // Create a new DbTransaction instance for this AtomicWrite
                    using (var tx = connection.BeginTransaction())
                    {
                        command.Transaction = tx;
                        
                        // Cast the payload to IImmutableList<IPersistentRepresentation>.
                        // Note that AtomicWrite.Payload property is declared as an object property,
                        // but it is always populated with an IImmutableList<IPersistentRepresentation>
                        // instance
                        var payload = (IImmutableList<IPersistentRepresentation>)message.Payload;
                        var persistentMessages = payload.ToArray();
                        foreach (var @event in persistentMessages)
                        {
                            // Get the serializer associated with the payload type,
                            // else use a default serializer
                            var serializer = _serialization.FindSerializerForType(
                                @event.Payload.GetType(), 
                                _defaultSerializer);
                            
                            // This WithTransport method call is important, it allows for proper
                            // local IActorRef serialization by switching the serialization information
                            // context during the serialization process
                            var (binary, manifest) = Akka.Serialization.Serialization.WithTransport(
                                system: _serialization.System, 
                                state: (@event.Payload, serializer), 
                                action: state =>
                                {
                                    var (thePayload, theSerializer) = state;
                                    var thisManifest = "";
                                    
                                    // There are two kinds of serializer when it comes to manifest
                                    // support, we have to support both of them for proper payload
                                    // serialization
                                    if (theSerializer is SerializerWithStringManifest stringManifest)
                                    {
                                        thisManifest = stringManifest.Manifest(thePayload);
                                    }
                                    else if (theSerializer.IncludeManifest)
                                    {
                                        thisManifest = thePayload.GetType().TypeQualifiedName();
                                    }
                                    
                                    // Return the serialized byte array and the manifest for the
                                    // serialized data
                                    return (theSerializer.ToBinary(thePayload), thisManifest);
                                });
                            
                            // Populate the SQL parameters
                            AddParameter(command, "@PersistenceId", DbType.String, @event.PersistenceId);
                            AddParameter(command, "@SequenceNr", DbType.Int64, @event.SequenceNr);
                            AddParameter(command, "@Timestamp", DbType.Int64, @event.Timestamp);
                            AddParameter(command, "@IsDeleted", DbType.Boolean, false);
                            AddParameter(command, "@Manifest", DbType.String, manifest);
                            AddParameter(command, "@Payload", DbType.Binary, binary);
                            AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);
                            
                            // Execute the SQL query
                            await command.ExecuteScalarAsync(cts.Token);
                            
                            // clear the parameters and reuse the DbCommand instance
                            command.Parameters.Clear();
                        }
                        // Commit the DbTransaction
                        tx.Commit();
                    }
                
                }
            }).ToArray();

            // Collect all exceptions raised for each failed writes and return them.
            var result = await Task<IImmutableList<Exception>>.Factory
                .ContinueWhenAll(writeTasks,
                    tasks => tasks.Select(t => t.IsFaulted 
                        ? TryUnwrapException(t.Exception) 
                        : null).ToImmutableList());

            return result;
        }
        // </WriteMessagesAsync>

        //<DeleteMessagesToAsync>
        protected sealed override async Task DeleteMessagesToAsync(
            string persistenceId,
            long toSequenceNr)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            {
                await connection.OpenAsync();
                
                using (var cts = CancellationTokenSource
                           .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
                {
                    // We will be using two DbCommands to complete this process
                    using (var deleteCommand = GetCommand(connection, DeleteBatchSql, _timeout))
                    using (var highestSeqNrCommand = 
                           GetCommand(connection, HighestSequenceNrSql, _timeout))
                    {
                        // Populate the SQL parameters
                        AddParameter(highestSeqNrCommand, "@PersistenceId", DbType.String, 
                            persistenceId);

                        AddParameter(deleteCommand, "@PersistenceId", DbType.String, persistenceId);
                        AddParameter(deleteCommand, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                        // Create a DbTransaction instance
                        using (var tx = connection.BeginTransaction())
                        {
                            deleteCommand.Transaction = tx;
                            highestSeqNrCommand.Transaction = tx;

                            // Execute the HighestSequenceNrSql SQL statement and fetch the current
                            // highest sequence number for the given persistence identifier
                            var res = await highestSeqNrCommand.ExecuteScalarAsync(cts.Token);
                            var highestSeqNr = res is long ? Convert.ToInt64(res) : 0L;

                            // Delete all events up to toSequenceNr both in the journal and
                            // metadata table
                            await deleteCommand.ExecuteNonQueryAsync(cts.Token);

                            // Update the metadata table to reflect the new highest sequence number,
                            // if the toSequenceNr is higher than our current highest sequence number
                            if (highestSeqNr <= toSequenceNr)
                            {
                                // Create a new DbCommand instance
                                using (var updateCommand = GetCommand(connection, UpdateSequenceNrSql, 
                                           _timeout))
                                {
                                    // This update is still part of the same transaction as everything
                                    // else in this process
                                    updateCommand.Transaction = tx;

                                    // Populate the SQL parameters
                                    AddParameter(
                                        updateCommand, "@PersistenceId", DbType.String, persistenceId);
                                    AddParameter(
                                        updateCommand, "@SequenceNr", DbType.Int64, highestSeqNr);

                                    // Execute the update SQL statement
                                    await updateCommand.ExecuteNonQueryAsync(cts.Token);
                                    tx.Commit();
                                }
                            }
                            else tx.Commit();
                        }
                    }
                }
            }
        }
        // </DeleteMessagesToAsync>

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AddParameter(DbCommand command, string parameterName, DbType parameterType, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = parameterType;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DbCommand GetCommand(DbConnection connection, string sql, TimeSpan timeout)
        {
            return new SqliteCommand
            {
                Connection = (SqliteConnection) connection, 
                CommandText = sql,
                CommandTimeout = (int)timeout.TotalSeconds
            };
        }
    }
}