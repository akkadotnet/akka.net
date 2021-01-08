//-----------------------------------------------------------------------
// <copyright file="QueryExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Sql.Common.Snapshot
{
    /// <summary>
    /// Flattened and serialized snapshot object used as intermediate representation
    /// before saving snapshot with metadata inside SQL Server database.
    /// </summary>
    public class SnapshotEntry
    {
        /// <summary>
        /// Persistence identifier of persistent actor, current snapshot relates to.
        /// </summary>
        public readonly string PersistenceId;

        /// <summary>
        /// Sequence number used to identify snapshot in it's persistent actor scope.
        /// </summary>
        public readonly long SequenceNr;

        /// <summary>
        /// Timestamp used to specify date, when the snapshot has been made.
        /// </summary>
        public readonly DateTime Timestamp;

        /// <summary>
        /// Stringified fully qualified CLR type name of the serialized object.
        /// </summary>
        public readonly string Manifest;

        /// <summary>
        /// Serialized object data.
        /// </summary>
        public readonly object Payload;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="timestamp">TBD</param>
        /// <param name="manifest">TBD</param>
        /// <param name="payload">TBD</param>
        public SnapshotEntry(string persistenceId, long sequenceNr, DateTime timestamp, string manifest, object payload)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
            Manifest = manifest;
            Payload = payload;
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class QueryConfiguration
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string SchemaName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string SnapshotTableName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PersistenceIdColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string SequenceNrColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string PayloadColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string ManifestColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string TimestampColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string SerializerIdColumnName;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly TimeSpan Timeout;

        /// <summary>
        /// The default serializer used when not type override matching is found
        /// </summary>
        public readonly string DefaultSerializer;

        /// <summary>
        /// Uses the CommandBehavior.SequentialAccess when creating the command, providing a performance improvement for reading large BLOBS.
        /// </summary>
        public bool UseSequentialAccess { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="schemaName">TBD</param>
        /// <param name="snapshotTableName">TBD</param>
        /// <param name="persistenceIdColumnName">TBD</param>
        /// <param name="sequenceNrColumnName">TBD</param>
        /// <param name="payloadColumnName">TBD</param>
        /// <param name="manifestColumnName">TBD</param>
        /// <param name="timestampColumnName">TBD</param>
        /// <param name="serializerIdColumnName">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="defaultSerializer">The default serializer used when not type override matching is found</param>
        /// <param name="useSequentialAccess">Uses the CommandBehavior.SequentialAccess when creating the command, providing a performance improvement for reading large BLOBS.</param>
        public QueryConfiguration(
            string schemaName,
            string snapshotTableName,
            string persistenceIdColumnName,
            string sequenceNrColumnName,
            string payloadColumnName,
            string manifestColumnName,
            string timestampColumnName,
            string serializerIdColumnName,
            TimeSpan timeout,
            string defaultSerializer,
            bool useSequentialAccess)
        {
            SchemaName = schemaName;
            SnapshotTableName = snapshotTableName;
            PersistenceIdColumnName = persistenceIdColumnName;
            SequenceNrColumnName = sequenceNrColumnName;
            PayloadColumnName = payloadColumnName;
            ManifestColumnName = manifestColumnName;
            TimestampColumnName = timestampColumnName;
            SerializerIdColumnName = serializerIdColumnName;
            Timeout = timeout;
            DefaultSerializer = defaultSerializer;
            UseSequentialAccess = useSequentialAccess;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string FullSnapshotTableName => string.IsNullOrEmpty(SchemaName) ? SnapshotTableName : SchemaName + "." + SnapshotTableName;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public interface ISnapshotQueryExecutor
    {
        /// <summary>
        /// Configuration settings for the current query executor.
        /// </summary>
        QueryConfiguration Configuration { get; }

        /// <summary>
        /// Deletes a single snapshot identified by it's persistent actor's <paramref name="persistenceId"/>,
        /// <paramref name="sequenceNr"/> and <paramref name="timestamp"/>.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="timestamp">TBD</param>
        /// <returns>TBD</returns>
        Task DeleteAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long sequenceNr, DateTime? timestamp);

        /// <summary>
        /// Deletes all snapshot matching persistent actor's <paramref name="persistenceId"/> as well as
        /// upper (inclusive) bounds of the both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="maxSequenceNr">TBD</param>
        /// <param name="maxTimestamp">TBD</param>
        /// <returns>TBD</returns>
        Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        /// <summary>
        /// Inserts a single snapshot represented by provided <see cref="SnapshotEntry"/> instance.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="snapshot">TBD</param>
        /// <param name="metadata">TBD</param>
        /// <returns>TBD</returns>
        Task InsertAsync(DbConnection connection, CancellationToken cancellationToken, object snapshot, SnapshotMetadata metadata);

        /// <summary>
        /// Selects a single snapshot identified by persistent actor's <paramref name="persistenceId"/>,
        /// matching upper (inclusive) bounds of both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// In case, when more than one snapshot matches specified criteria, one with the highest sequence number will be selected.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="maxSequenceNr">TBD</param>
        /// <param name="maxTimestamp">TBD</param>
        /// <returns>TBD</returns>
        Task<SelectedSnapshot> SelectSnapshotAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        Task CreateTableAsync(DbConnection connection, CancellationToken cancellationToken);
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class AbstractQueryExecutor : ISnapshotQueryExecutor
    {
        /// <summary>
        /// TBD
        /// </summary>
        protected Akka.Serialization.Serialization Serialization;

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string SelectSnapshotSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string DeleteSnapshotSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string DeleteSnapshotRangeSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string InsertSnapshotSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected abstract string CreateSnapshotTableSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configuration">TBD</param>
        /// <param name="serialization">TBD</param>
        protected AbstractQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization)
        {
            Configuration = configuration;
            Serialization = serialization;

            SelectSnapshotSql = $@"
                SELECT {Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName}, 
                    {Configuration.TimestampColumnName}, 
                    {Configuration.ManifestColumnName}, 
                    {Configuration.PayloadColumnName},
                    {Configuration.SerializerIdColumnName}
                FROM {Configuration.FullSnapshotTableName} 
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId 
                    AND {Configuration.SequenceNrColumnName} <= @SequenceNr
                    AND {Configuration.TimestampColumnName} <= @Timestamp
                ORDER BY {Configuration.SequenceNrColumnName} DESC
                LIMIT 1";

            DeleteSnapshotSql = $@"
                DELETE FROM {Configuration.FullSnapshotTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId
                    AND {Configuration.SequenceNrColumnName} = @SequenceNr";

            DeleteSnapshotRangeSql = $@"
                DELETE FROM {Configuration.FullSnapshotTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId
                    AND {Configuration.SequenceNrColumnName} <= @SequenceNr
                    AND {Configuration.TimestampColumnName} <= @Timestamp";

            InsertSnapshotSql = $@"
                INSERT INTO {Configuration.FullSnapshotTableName} (
                    {Configuration.PersistenceIdColumnName}, 
                    {Configuration.SequenceNrColumnName}, 
                    {Configuration.TimestampColumnName}, 
                    {Configuration.ManifestColumnName}, 
                    {Configuration.PayloadColumnName},
                    {Configuration.SerializerIdColumnName}) VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Payload, @SerializerId)";
        }

        /// <summary>
        /// TBD
        /// </summary>
        public QueryConfiguration Configuration { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="timestamp">TBD</param>
        /// <param name="command">TBD</param>
        protected virtual void SetTimestampParameter(DateTime timestamp, DbCommand command) => AddParameter(command, "@Timestamp", DbType.DateTime2, timestamp);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="command">TBD</param>
        protected virtual void SetSequenceNrParameter(long sequenceNr, DbCommand command) => AddParameter(command, "@SequenceNr", DbType.Int64, sequenceNr);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="persistenceId">TBD</param>
        /// <param name="command">TBD</param>
        protected virtual void SetPersistenceIdParameter(string persistenceId, DbCommand command) => AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="snapshot">TBD</param>
        /// <param name="command">TBD</param>
        protected virtual void SetPayloadParameter(object snapshot, DbCommand command)
        {
            var snapshotType = snapshot.GetType();
            var serializer = Serialization.FindSerializerForType(snapshotType, Configuration.DefaultSerializer);
            // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
            var binary = Akka.Serialization.Serialization.WithTransport(Serialization.System, () => serializer.ToBinary(snapshot));
            AddParameter(command, "@Payload", DbType.Binary, binary);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="snapshot">TBD</param>
        /// <param name="command">TBD</param>
        protected virtual void SetManifestParameters(object snapshot, DbCommand command)
        {
            var snapshotType = snapshot.GetType();
            var serializer = Serialization.FindSerializerForType(snapshotType, Configuration.DefaultSerializer);

            string manifest = "";
            if (serializer is SerializerWithStringManifest)
            {
                manifest = ((SerializerWithStringManifest)serializer).Manifest(snapshot);
            }
            else
            {
                if (serializer.IncludeManifest)
                {
                    manifest = snapshotType.TypeQualifiedName();
                }
            }
            AddParameter(command, "@Manifest", DbType.String, manifest);
            AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="sequenceNr">TBD</param>
        /// <param name="timestamp">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task DeleteAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long sequenceNr,
            DateTime? timestamp)
        {
            var sql = timestamp.HasValue
                ? DeleteSnapshotRangeSql + " AND { Configuration.TimestampColumnName} = @Timestamp"
                : DeleteSnapshotSql;

            using (var command = GetCommand(connection, sql))
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;

                SetPersistenceIdParameter(persistenceId, command);
                SetSequenceNrParameter(sequenceNr, command);

                if (timestamp.HasValue)
                {
                    SetTimestampParameter(timestamp.Value, command);
                }

                await command.ExecuteNonQueryAsync(cancellationToken);

                tx.Commit();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="maxSequenceNr">TBD</param>
        /// <param name="maxTimestamp">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId,
            long maxSequenceNr, DateTime maxTimestamp)
        {
            using (var command = GetCommand(connection, DeleteSnapshotRangeSql))
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;

                SetPersistenceIdParameter(persistenceId, command);
                SetSequenceNrParameter(maxSequenceNr, command);
                SetTimestampParameter(maxTimestamp, command);

                await command.ExecuteNonQueryAsync(cancellationToken);

                tx.Commit();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="snapshot">TBD</param>
        /// <param name="metadata">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task InsertAsync(DbConnection connection, CancellationToken cancellationToken, object snapshot, SnapshotMetadata metadata)
        {
            using (var command = GetCommand(connection, InsertSnapshotSql))
            using(var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;

                SetPersistenceIdParameter(metadata.PersistenceId, command);
                SetSequenceNrParameter(metadata.SequenceNr, command);
                SetTimestampParameter(metadata.Timestamp, command);
                SetManifestParameters(snapshot, command);
                SetPayloadParameter(snapshot, command);

                await command.ExecuteNonQueryAsync(cancellationToken);

                tx.Commit();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="maxSequenceNr">TBD</param>
        /// <param name="maxTimestamp">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task<SelectedSnapshot> SelectSnapshotAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId,
            long maxSequenceNr, DateTime maxTimestamp)
        {
            using (var command = GetCommand(connection, SelectSnapshotSql))
            {
                SetPersistenceIdParameter(persistenceId, command);
                SetSequenceNrParameter(maxSequenceNr, command);
                SetTimestampParameter(maxTimestamp, command);

                CommandBehavior commandBehavior;

                if (Configuration.UseSequentialAccess)
                {
                    commandBehavior = CommandBehavior.SequentialAccess;
                }
                else
                {
                    commandBehavior = CommandBehavior.Default;
                }

                using (var reader = await command.ExecuteReaderAsync(commandBehavior, cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        return ReadSnapshot(reader);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task CreateTableAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, CreateSnapshotTableSql))
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;
                await command.ExecuteNonQueryAsync(cancellationToken);
                tx.Commit();
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="sql">TBD</param>
        /// <returns>TBD</returns>
        protected DbCommand GetCommand(DbConnection connection, string sql)
        {
            var command = CreateCommand(connection);
            command.CommandText = sql;
            command.CommandTimeout = (int)Configuration.Timeout.TotalSeconds;
            return command;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="command">TBD</param>
        /// <param name="parameterName">TBD</param>
        /// <param name="parameterType">TBD</param>
        /// <param name="value">TBD</param>
        /// <returns>TBD</returns>
        protected void AddParameter(DbCommand command, string parameterName, DbType parameterType, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = parameterType;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <returns>TBD</returns>
        protected abstract DbCommand CreateCommand(DbConnection connection);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reader">TBD</param>
        /// <returns>TBD</returns>
        protected virtual SelectedSnapshot ReadSnapshot(DbDataReader reader)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var timestamp = reader.GetDateTime(2);

            var metadata = new SnapshotMetadata(persistenceId, sequenceNr, timestamp);
            var snapshot = GetSnapshot(reader);

            return new SelectedSnapshot(metadata, snapshot);
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reader">TBD</param>
        /// <returns>TBD</returns>
        protected object GetSnapshot(DbDataReader reader)
        {
            var manifest = reader.GetString(3);
            var binary = (byte[])reader[4];

            object obj;
            if (reader.IsDBNull(5))
            {
                var type = Type.GetType(manifest, true);
                // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                var serializer = Serialization.FindSerializerForType(type, Configuration.DefaultSerializer);
                obj = Akka.Serialization.Serialization.WithTransport(Serialization.System, () => serializer.FromBinary(binary, type));
            }
            else
            {
                var serializerId = reader.GetInt32(5);
                obj = Serialization.Deserialize(binary, serializerId, manifest);
            }

            return obj;
        }
    }
}
