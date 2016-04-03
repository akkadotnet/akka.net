using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

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

        public SnapshotEntry(string persistenceId, long sequenceNr, DateTime timestamp, string manifest, object payload)
        {
            PersistenceId = persistenceId;
            SequenceNr = sequenceNr;
            Timestamp = timestamp;
            Manifest = manifest;
            Payload = payload;
        }
    }

    public class QueryConfiguration
    {
        public readonly string SchemaName;
        public readonly string SnapshotTableName;
        public readonly string PersistenceIdColumnName;
        public readonly string SequenceNrColumnName;
        public readonly string PayloadColumnName;
        public readonly string ManifestColumnName;
        public readonly string TimestampColumnName;

        public readonly TimeSpan Timeout;

        public QueryConfiguration(
            string schemaName,
            string snapshotTableName,
            string persistenceIdColumnName,
            string sequenceNrColumnName,
            string payloadColumnName,
            string manifestColumnName,
            string timestampColumnName,
            TimeSpan timeout)
        {
            SchemaName = schemaName;
            SnapshotTableName = snapshotTableName;
            PersistenceIdColumnName = persistenceIdColumnName;
            SequenceNrColumnName = sequenceNrColumnName;
            PayloadColumnName = payloadColumnName;
            ManifestColumnName = manifestColumnName;
            TimestampColumnName = timestampColumnName;
            Timeout = timeout;
        }

        public string FullSnapshotTableName => string.IsNullOrEmpty(SchemaName) ? SnapshotTableName : SchemaName + "." + SnapshotTableName;
    }

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
        Task DeleteAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long sequenceNr, DateTime? timestamp);

        /// <summary>
        /// Deletes all snapshot matching persistent actor's <paramref name="persistenceId"/> as well as 
        /// upper (inclusive) bounds of the both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// </summary>
        Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        /// <summary>
        /// Inserts a single snapshot represented by provided <see cref="SnapshotEntry"/> instance.
        /// </summary>
        Task InsertAsync(DbConnection connection, CancellationToken cancellationToken, SnapshotEntry snapshotEntry);

        /// <summary>
        /// Selects a single snapshot identified by persistent actor's <paramref name="persistenceId"/>,
        /// matching upper (inclusive) bounds of both <paramref name="maxSequenceNr"/> and <paramref name="maxTimestamp"/>.
        /// In case, when more than one snapshot matches specified criteria, one with the highest sequence number will be selected.
        /// </summary>
        Task<SelectedSnapshot> SelectSnapshotAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long maxSequenceNr, DateTime maxTimestamp);

        Task CreateTableAsync(DbConnection connection, CancellationToken cancellationToken);
    }

    public abstract class AbstractQueryExecutor : ISnapshotQueryExecutor
    {
        private readonly Akka.Serialization.Serialization _serialization;

        protected virtual string SelectSnapshotSql { get; }
        protected virtual string DeleteSnapshotSql { get; }
        protected virtual string DeleteSnapshotRangeSql { get; }
        protected virtual string InsertSnapshotSql { get; }
        protected abstract string CreateSnapshotTableSql { get; }

        protected AbstractQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization)
        {
            Configuration = configuration;
            _serialization = serialization;

            SelectSnapshotSql = $@"
                SELECT {Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName}, 
                    {Configuration.TimestampColumnName}, 
                    {Configuration.ManifestColumnName}, 
                    {Configuration.PayloadColumnName}   
                FROM {Configuration.FullSnapshotTableName} 
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId 
                    AND {Configuration.SequenceNrColumnName} <= @SequenceNr
                    AND {Configuration.TimestampColumnName} <= @Timestamp
                ORDER BY {Configuration.SequenceNrColumnName} DESC";

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
                    {Configuration.PayloadColumnName}) VALUES (@PersistenceId, @SequenceNr, @Timestamp, @Manifest, @Payload)";
        }

        public QueryConfiguration Configuration { get; }

        protected virtual void SetTimestampParameter(DateTime timestamp, DbCommand command) => AddParameter(command, "@Timestamp", DbType.DateTime2, timestamp);
        protected virtual void SetSequenceNrParameter(long sequenceNr, DbCommand command) => AddParameter(command, "@SequenceNr", DbType.Int64, sequenceNr);
        protected virtual void SetPersistenceIdParameter(string persistenceId, DbCommand command) => AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
        protected virtual void SetPayloadParameter(object payload, DbCommand command) => AddParameter(command, "@Payload", DbType.Binary, payload);
        protected virtual void SetManifestParameter(string manifest, DbCommand command) => AddParameter(command, "@Manifest", DbType.String, manifest);

        public async Task DeleteAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long sequenceNr,
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

        public async Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId,
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

        public async Task InsertAsync(DbConnection connection, CancellationToken cancellationToken, SnapshotEntry snapshotEntry)
        {
            using (var command = GetCommand(connection, InsertSnapshotSql))
            using(var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;

                SetPersistenceIdParameter(snapshotEntry.PersistenceId, command);
                SetSequenceNrParameter(snapshotEntry.SequenceNr, command);
                SetTimestampParameter(snapshotEntry.Timestamp, command);
                SetManifestParameter(snapshotEntry.Manifest, command);
                SetPayloadParameter(snapshotEntry.Payload, command);

                await command.ExecuteNonQueryAsync(cancellationToken);

                tx.Commit();
            }
        }

        public async Task<SelectedSnapshot> SelectSnapshotAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId,
            long maxSequenceNr, DateTime maxTimestamp)
        {
            using (var command = GetCommand(connection, SelectSnapshotSql))
            {

                SetPersistenceIdParameter(persistenceId, command);
                SetSequenceNrParameter(maxSequenceNr, command);
                SetTimestampParameter(maxTimestamp, command);
                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        return Map(reader);
                    }
                }
            }

            return null;
        }

        public async Task CreateTableAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, CreateSnapshotTableSql))
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;
                await command.ExecuteNonQueryAsync(cancellationToken);
                tx.Commit();
            }
        }

        protected DbCommand GetCommand(DbConnection connection, string sql)
        {
            var command = CreateCommand(connection);
            command.CommandText = sql;
            command.CommandTimeout = (int)Configuration.Timeout.TotalSeconds;
            return command;
        }

        protected void AddParameter(DbCommand command, string parameterName, DbType parameterType, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = parameterType;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }

        protected abstract DbCommand CreateCommand(DbConnection connection);

        protected virtual SelectedSnapshot Map(DbDataReader reader)
        {
            var persistenceId = reader.GetString(0);
            var sequenceNr = reader.GetInt64(1);
            var timestamp = reader.GetDateTime(2);

            var metadata = new SnapshotMetadata(persistenceId, sequenceNr, timestamp);
            var snapshot = GetSnapshot(reader);

            return new SelectedSnapshot(metadata, snapshot);
        }

        protected object GetSnapshot(DbDataReader reader)
        {
            var type = Type.GetType(reader.GetString(3), true);
            var serializer = _serialization.FindSerializerForType(type);
            var binary = (byte[])reader[4];

            var obj = serializer.FromBinary(binary, type);

            return obj;
        }
    }
}