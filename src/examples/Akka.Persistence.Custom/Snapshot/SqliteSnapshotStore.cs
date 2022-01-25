// //-----------------------------------------------------------------------
// // <copyright file="SqliteSnapshotStore.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Persistence.Snapshot;
using Akka.Serialization;
using Akka.Util;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Custom.Snapshot
{
    public class SqliteSnapshotStore: SnapshotStore, IWithUnboundedStash
    {
        //<CreateSnapshotTableSql>
        private const string CreateSnapshotTableSql = @"
                CREATE TABLE IF NOT EXISTS snapshot (
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    timestamp INTEGER(8) NOT NULL,
                    manifest VARCHAR(255) NOT NULL,
                    payload BLOB NOT NULL,
                    serializer_id INTEGER(4),
                    PRIMARY KEY (persistence_id, sequence_nr));";
        //</CreateSnapshotTableSql>

        //<SelectSnapshotSql>
        private const string SelectSnapshotSql = @"
            SELECT persistence_id,
                sequence_nr, 
                timestamp,
                manifest, 
                payload,
                serializer_id
            FROM snapshot 
            WHERE persistence_id = @PersistenceId 
                AND sequence_nr <= @SequenceNr
                AND timestamp <= @Timestamp
            ORDER BY sequence_nr DESC
            LIMIT 1";
        //</SelectSnapshotSql>

        //<DeleteSnapshotSql>
        private const string DeleteSnapshotSql = @"
            DELETE FROM snapshot
            WHERE persistence_id = @PersistenceId
                AND sequence_nr = @SequenceNr";
        //</DeleteSnapshotSql>

        //<DeleteSnapshotRangeSql>
        private const string DeleteSnapshotRangeSql = @"
            DELETE FROM snapshot
            WHERE persistence_id = @PersistenceId
                AND sequence_nr <= @SequenceNr
                AND timestamp <= @Timestamp";
        //</DeleteSnapshotRangeSql>

        //<InsertSnapshotSql>
        private const string InsertSnapshotSql = @"
                UPDATE snapshot
                SET timestamp = @Timestamp, 
                    manifest = @Manifest,
                    payload = @Payload, 
                    serializer_id = @SerializerId
                WHERE persistence_id = @PersistenceId AND sequence_nr = @SequenceNr;

                INSERT OR IGNORE INTO snapshot 
                    (persistence_id, sequence_nr, timestamp, manifest, payload, serializer_id)
                VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Payload, @SerializerId)";
        //</InsertSnapshotSql>
        
        private readonly SnapshotStoreSettings _settings;
        private readonly string _connectionString;
        private readonly TimeSpan _timeout;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly string _defaultSerializer;
        private readonly ILoggingAdapter _log;
        private readonly CancellationTokenSource _pendingRequestsCancellation;

        public SqliteSnapshotStore()
        {
            _settings = new SnapshotStoreSettings(SqlitePersistence.Get(Context.System).SnapshotConfig);
            _connectionString = _settings.ConnectionString;
            _timeout = _settings.ConnectionTimeout;
            _defaultSerializer = _settings.DefaultSerializer;
            
            _serialization = Context.System.Serialization;
            _pendingRequestsCancellation = new CancellationTokenSource();
            _log = Context.GetLogger();
        }
        
        public IStash Stash { get; set; }

        //<Startup>
        protected override void PreStart()
        {
            base.PreStart();
            if (_settings.AutoInitialize)
            {
                // Call the Initialize method and pipe the result back to signal that
                // database schemas are ready to use, if it needs to be initialized
                Initialize().PipeTo(Self);
            
                // WaitingForInitialization receive handler will wait for a success/fail
                // result back from the Initialize method
                BecomeStacked(WaitingForInitialization);
            }
        }
        
        protected override void PostStop()
        {
            base.PostStop();

            // stop all operations executed in the background
            _pendingRequestsCancellation.Cancel();
        }

        private async Task<object> Initialize()
        {
            // No database initialization needed, the user explicitly asked us not to initialize anything.
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
                    
                    using (var command = GetCommand(connection, CreateSnapshotTableSql, _timeout))
                    using (var tx = connection.BeginTransaction())
                    {
                        command.Transaction = tx;
                        await command.ExecuteNonQueryAsync(cts.Token);
                        tx.Commit();
                    }
                    
                    return Status.Success.Instance;
                }
            }
            catch (Exception e)
            {
                return new Status.Failure(e);
            }
        }

        private bool WaitingForInitialization(object message)
        {
            switch(message)
            {
                // Tables are already created or successfully created all needed tables
                case Status.Success _:
                    UnbecomeStacked();
                    // Unstash all messages received when we were initializing our tables
                    Stash.UnstashAll();
                    return true;
                
                case Status.Failure msg:
                    // Failed creating tables. Log an error and stop the actor.
                    _log.Error(msg.Cause, "Error during snapshot store initialization");
                    Context.Stop(Self);
                    return true;
                
                default:
                    // By default, stash all received messages while we're waiting for the
                    // Initialize method.
                    Stash.Stash();
                    return true;
            }
        }
        //</Startup>
        
        //<LoadAsync>
        protected sealed override async Task<SelectedSnapshot> LoadAsync(
            string persistenceId,
            SnapshotSelectionCriteria criteria)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                
                // Create new DbCommand instance
                using (var command = GetCommand(connection, SelectSnapshotSql, _timeout))
                {
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, criteria.MaxSequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, criteria.MaxTimeStamp);

                    // Create a DbDataReader to sequentially read the returned query result
                    using (var reader = await command.ExecuteReaderAsync(
                               CommandBehavior.SequentialAccess,
                               cts.Token))
                    {
                        // Return null if no snapshot is found
                        if (!await reader.ReadAsync(cts.Token)) 
                            return null;
                        
                        var id = reader.GetString(0);
                        var sequenceNr = reader.GetInt64(1);
                        var timestamp = reader.GetDateTime(2);

                        var metadata = new SnapshotMetadata(id, sequenceNr, timestamp);
                            
                        var manifest = reader.GetString(3);
                        var binary = (byte[])reader[4];
                        
                        var serializerId = reader.GetInt32(5);
                        
                        // Deserialize the snapshot payload using the data we read from the reader
                        var snapshot = _serialization.Deserialize(binary, serializerId, manifest);

                        return new SelectedSnapshot(metadata, snapshot);
                    }
                }
            }
        }
        //</LoadAsync>

        //<SaveAsync>
        protected sealed override async Task SaveAsync(
            SnapshotMetadata metadata,
            object snapshot)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                
                // Create new DbCommand instance
                using (var command = GetCommand(connection, InsertSnapshotSql, _timeout)) 
                // Create a new DbTransaction instance
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    
                    var snapshotType = snapshot.GetType();
                    
                    // Get the serializer associated with the payload type,
                    // else use a default serializer
                    var serializer = _serialization.FindSerializerForType(
                        objectType: snapshotType,
                        defaultSerializerName: _defaultSerializer);

                    // This WithTransport method call is important, it allows for proper
                    // local IActorRef serialization by switching the serialization information
                    // context during the serialization process
                    var (binary, manifest) = Akka.Serialization.Serialization.WithTransport(
                        system: _serialization.System,
                        state: (snapshot, serializer),
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
                    AddParameter(command, "@PersistenceId", DbType.String, metadata.PersistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, metadata.SequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, metadata.Timestamp);
                    AddParameter(command, "@Manifest", DbType.String, manifest);
                    AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);
                    AddParameter(command, "@Payload", DbType.Binary, binary);

                    // Execute the SQL query
                    await command.ExecuteNonQueryAsync(cts.Token);

                    // Commit the DbTransaction
                    tx.Commit();
                }
            }
        }
        //</SaveAsync>

        //<DeleteAsync>
        protected sealed override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))    
            {
                await connection.OpenAsync(cts.Token);
                var timestamp = metadata.Timestamp != DateTime.MinValue 
                    ? metadata.Timestamp 
                    : (DateTime?)null;
                
                var sql = timestamp.HasValue
                    ? DeleteSnapshotRangeSql + " AND { Configuration.TimestampColumnName} = @Timestamp"
                    : DeleteSnapshotSql;

                // Create new DbCommand instance
                using (var command = GetCommand(connection, sql, _timeout))
                // Create a new DbTransaction instance
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, metadata.PersistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, metadata.SequenceNr);
                    if (timestamp.HasValue)
                    {
                        AddParameter(command, "@Timestamp", DbType.DateTime2, metadata.Timestamp);
                    }

                    // Execute the SQL query
                    await command.ExecuteNonQueryAsync(cts.Token);

                    // Commit the DbTransaction
                    tx.Commit();
                }
            }
        }
        //</DeleteAsync>

        //<DeleteAsync2>
        protected sealed override async Task DeleteAsync(
            string persistenceId, 
            SnapshotSelectionCriteria criteria)
        {
            // Create a new DbConnection instance
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource
                       .CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                
                // Create new DbCommand instance
                using (var command = GetCommand(connection, DeleteSnapshotRangeSql, _timeout))
                // Create a new DbTransaction instance
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    
                    // Populate the SQL parameters
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, criteria.MaxSequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, criteria.MaxTimeStamp);
                    
                    // Execute the SQL query
                    await command.ExecuteNonQueryAsync(cts.Token);

                    // Commit the DbTransaction
                    tx.Commit();
                }
            }
        }
        //</DeleteAsync2>

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