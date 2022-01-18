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
        private const string CreateSnapshotTableSql = @"
                CREATE TABLE IF NOT EXISTS snapshot (
                    persistence_id VARCHAR(255) NOT NULL,
                    sequence_nr INTEGER(8) NOT NULL,
                    timestamp INTEGER(8) NOT NULL,
                    manifest VARCHAR(255) NOT NULL,
                    payload BLOB NOT NULL,
                    serializer_id INTEGER(4),
                    PRIMARY KEY (persistence_id, sequence_nr));";

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

        private const string DeleteSnapshotSql = @"
            DELETE FROM snapshot
            WHERE persistence_id = @PersistenceId
                AND sequence_nr = @SequenceNr";

        private const string DeleteSnapshotRangeSql = @"
            DELETE FROM snapshot
            WHERE persistence_id = @PersistenceId
                AND sequence_nr <= @SequenceNr
                AND timestamp <= @Timestamp";

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

        protected override void PreStart()
        {
            base.PreStart();
            if (_settings.AutoInitialize)
            {
                Initialize().PipeTo(Self);
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
            try
            {
                using (var connection = new SqliteConnection(_connectionString))
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
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
                case Status.Success _:
                    UnbecomeStacked();
                    Stash.UnstashAll();
                    return true;
                case Status.Failure msg:
                    _log.Error(msg.Cause, "Error during snapshot store initialization");
                    Context.Stop(Self);
                    return true;
                default:
                    Stash.Stash();
                    return true;
            }
        }
        
        protected sealed override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                using (var command = GetCommand(connection, SelectSnapshotSql, _timeout))
                {
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, criteria.MaxSequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, criteria.MaxTimeStamp);

                    using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cts.Token))
                    {
                        if (!await reader.ReadAsync(cts.Token)) 
                            return null;
                        
                        var id = reader.GetString(0);
                        var sequenceNr = reader.GetInt64(1);
                        var timestamp = reader.GetDateTime(2);

                        var metadata = new SnapshotMetadata(id, sequenceNr, timestamp);
                            
                        var manifest = reader.GetString(3);
                        var binary = (byte[])reader[4];

                        object snapshot;
                        if (reader.IsDBNull(5))
                        {
                            var type = Type.GetType(manifest, true);
                            var serializer = _serialization.FindSerializerForType(type, _defaultSerializer);
                            snapshot = Akka.Serialization.Serialization.WithTransport(
                                _serialization.System, 
                                () => serializer.FromBinary(binary, type));
                        }
                        else
                        {
                            var serializerId = reader.GetInt32(5);
                            snapshot = _serialization.Deserialize(binary, serializerId, manifest);
                        }

                        return new SelectedSnapshot(metadata, snapshot);
                    }
                }
            }

        }

        protected sealed override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                //await QueryExecutor.InsertAsync(connection, nestedCancellationTokenSource.Token, snapshot, metadata);
                using (var command = GetCommand(connection, InsertSnapshotSql, _timeout))
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    AddParameter(command, "@PersistenceId", DbType.String, metadata.PersistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, metadata.SequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, metadata.Timestamp);

                    var snapshotType = snapshot.GetType();
                    var serializer = _serialization.FindSerializerForType(snapshotType, _defaultSerializer);

                    var manifest = "";
                    if (serializer is SerializerWithStringManifest serializerWithStringManifest)
                    {
                        manifest = serializerWithStringManifest.Manifest(snapshot);
                    }
                    else if (serializer.IncludeManifest)
                    {
                        manifest = snapshotType.TypeQualifiedName();
                    }
                    AddParameter(command, "@Manifest", DbType.String, manifest);
                    AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);
                    
                    var binary = Akka.Serialization.Serialization.WithTransport(_serialization.System, () => serializer.ToBinary(snapshot));
                    AddParameter(command, "@Payload", DbType.Binary, binary);

                    await command.ExecuteNonQueryAsync(cts.Token);

                    tx.Commit();
                }
            }
        }

        protected sealed override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))    
            {
                await connection.OpenAsync(cts.Token);
                var timestamp = metadata.Timestamp != DateTime.MinValue ? metadata.Timestamp : (DateTime?)null;
                
                var sql = timestamp.HasValue
                    ? DeleteSnapshotRangeSql + " AND { Configuration.TimestampColumnName} = @Timestamp"
                    : DeleteSnapshotSql;

                using (var command = GetCommand(connection, sql, _timeout))
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    AddParameter(command, "@PersistenceId", DbType.String, metadata.PersistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, metadata.SequenceNr);

                    if (timestamp.HasValue)
                    {
                        AddParameter(command, "@Timestamp", DbType.DateTime2, metadata.Timestamp);
                    }

                    await command.ExecuteNonQueryAsync(cts.Token);

                    tx.Commit();
                }
                
            }
        }

        protected sealed override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using (var connection = new SqliteConnection(_connectionString))
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(_pendingRequestsCancellation.Token))
            {
                await connection.OpenAsync(cts.Token);
                using (var command = GetCommand(connection, DeleteSnapshotRangeSql, _timeout))
                using (var tx = connection.BeginTransaction())
                {
                    command.Transaction = tx;
                    AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                    AddParameter(command, "@SequenceNr", DbType.Int64, criteria.MaxSequenceNr);
                    AddParameter(command, "@Timestamp", DbType.DateTime2, criteria.MaxTimeStamp);
                    
                    await command.ExecuteNonQueryAsync(cts.Token);

                    tx.Commit();
                }
                
            }
        }

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