//-----------------------------------------------------------------------
// <copyright file="QueryExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Sql.Common.Queries;

namespace Akka.Persistence.Sql.Common.Journal
{
    /// <summary>
    /// SQL query builder used for generating queries required to perform journal's tasks.
    /// </summary>
    public interface IJournalQueryExecutor
    {
        /// <summary>
        /// Configuration settings for the current query executor.
        /// </summary>
        QueryConfiguration Configuration { get; }

        /// <summary>
        /// Asynchronously returns all persistence Ids.
        /// </summary>
        /// <returns></returns>
        Task<ImmutableArray<string>> SelectAllPersistenceIdsAsync(DbConnection connection, CancellationToken cancellationToken);

        /// <summary>
        /// Asynchronously replays a <paramref name="callback"/> on all selected events for provided 
        /// <paramref name="persistenceId"/>, within boundaries of <paramref name="fromSequenceNr"/> 
        /// and <paramref name="toSequenceNr"/> up to <paramref name="max"/> number of events.
        /// </summary>
        Task SelectByPersistenceIdAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> callback);

        /// <summary>
        /// Asynchronously replays <paramref name="callback"/> on all selected events, which have been tagged using 
        /// provided <paramref name="tag"/>, within boundaries of <paramref name="fromOffset"/> and 
        /// <paramref name="toOffset"/>, up to <paramref name="max"/> number of elements.
        /// Returns highest sequence number from selected events.
        /// </summary>
        Task<long> SelectByTagAsync(DbConnection connection, CancellationToken cancellationToken, string tag, long fromOffset, long toOffset, long max, Action<ReplayedTaggedMessage> callback);

        /// <summary>
        /// Asynchronously returns single number considered as the highest sequence number in current journal for the provided <paramref name="persistenceId"/>.
        /// </summary>
        Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId);

        /// <summary>
        /// Asynchronously inserts a collection of events and theirs tags into a journal table.
        /// </summary>
        Task InsertBatchAsync(DbConnection connection, CancellationToken cancellationToken, WriteJournalBatch write);

        /// <summary>
        /// Asynchronously (permamanently) deletes all events with given <paramref name="persistenceId"/>, up to provided <paramref name="toSequenceNr"/>.
        /// </summary>
        Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long toSequenceNr);

        /// <summary>
        /// Asynchronously tries to create new event journal and metadata tables.
        /// </summary>
        Task CreateTablesAsync(DbConnection connection, CancellationToken cancellationToken);

        [Obsolete("This method will be obsoleted. Use Akka.Persistence.Query read journal features instead.")]
        Task SelectEventsAsync(DbConnection connection, CancellationToken cancellationToken, IEnumerable<IHint> hints, Action<IPersistentRepresentation> callback);
    }

#if SERIALIZATION
    [Serializable]
#endif
    public class QueryConfiguration
    {
        public readonly string TagsColumnName;

        public readonly string SchemaName;
        public readonly string JournalEventsTableName;
        public readonly string MetaTableName;
        public readonly string PersistenceIdColumnName;
        public readonly string SequenceNrColumnName;
        public readonly string PayloadColumnName;
        public readonly string ManifestColumnName;
        public readonly string TimestampColumnName;
        public readonly string IsDeletedColumnName;

        public readonly TimeSpan Timeout;

        public QueryConfiguration(
            string schemaName,
            string journalEventsTableName,
            string metaTableName,
            string persistenceIdColumnName,
            string sequenceNrColumnName,
            string payloadColumnName,
            string manifestColumnName,
            string timestampColumnName,
            string isDeletedColumnName,
            string tagsColumnName,
            TimeSpan timeout)
        {
            SchemaName = schemaName;
            JournalEventsTableName = journalEventsTableName;
            MetaTableName = metaTableName;
            PersistenceIdColumnName = persistenceIdColumnName;
            SequenceNrColumnName = sequenceNrColumnName;
            PayloadColumnName = payloadColumnName;
            ManifestColumnName = manifestColumnName;
            TimestampColumnName = timestampColumnName;
            IsDeletedColumnName = isDeletedColumnName;
            Timeout = timeout;
            TagsColumnName = tagsColumnName;
        }

        public string FullJournalTableName => string.IsNullOrEmpty(SchemaName) ? JournalEventsTableName : SchemaName + "." + JournalEventsTableName;
        public string FullMetaTableName => string.IsNullOrEmpty(SchemaName) ? MetaTableName : SchemaName + "." + MetaTableName;
    }

    public abstract class AbstractQueryExecutor : IJournalQueryExecutor
    {
        // indexes of particular fields returned from all events queries 
        // they must match `allEventColumnNames` order
        protected const int PersistenceIdIndex = 0;
        protected const int SequenceNrIndex = 1;
        protected const int TimestampIndex = 2;
        protected const int IsDeletedIndex = 3;
        protected const int ManifestIndex = 4;
        protected const int PayloadIndex = 5;

        protected static readonly string LongTypeName = typeof(long).FullName;

        protected Akka.Serialization.Serialization Serialization;
        protected ITimestampProvider TimestampProvider;
        public QueryConfiguration Configuration { get; }

        protected AbstractQueryExecutor(QueryConfiguration configuration, Akka.Serialization.Serialization serialization, ITimestampProvider timestampProvider)
        {
            TimestampProvider = timestampProvider;
            Serialization = serialization;
            Configuration = configuration;

            var allEventColumnNames = $@"
                e.{Configuration.PersistenceIdColumnName} as PersistenceId, 
                e.{Configuration.SequenceNrColumnName} as SequenceNr, 
                e.{Configuration.TimestampColumnName} as Timestamp, 
                e.{Configuration.IsDeletedColumnName} as IsDeleted, 
                e.{Configuration.ManifestColumnName} as Manifest, 
                e.{Configuration.PayloadColumnName} as Payload";

            AllPersistenceIdsSql = $@"
                SELECT DISTINCT e.{Configuration.PersistenceIdColumnName} as PersistenceId 
                FROM {Configuration.FullJournalTableName} e;";

            HighestSequenceNrSql = $@"
                SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT e.{Configuration.SequenceNrColumnName} as SeqNr FROM {Configuration.FullJournalTableName} e WHERE e.{Configuration.PersistenceIdColumnName} = @PersistenceId
                    UNION
                    SELECT m.{Configuration.SequenceNrColumnName} as SeqNr FROM {Configuration.FullMetaTableName} m WHERE m.{Configuration.PersistenceIdColumnName} = @PersistenceId) as u";

            DeleteBatchSql = $@"
                DELETE FROM {Configuration.FullJournalTableName} 
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId AND {Configuration.SequenceNrColumnName} <= @ToSequenceNr;";

            UpdateSequenceNrSql = $@"
                INSERT INTO {Configuration.FullMetaTableName} ({Configuration.PersistenceIdColumnName}, {Configuration.SequenceNrColumnName}) 
                VALUES (@PersistenceId, @SequenceNr);";

            ByPersistenceIdSql =
                $@"
                SELECT {allEventColumnNames}
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.PersistenceIdColumnName} = @PersistenceId
                AND e.{Configuration.SequenceNrColumnName} BETWEEN @FromSequenceNr AND @ToSequenceNr;";

            ByTagSql =
                $@"
                SELECT {allEventColumnNames}
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.TagsColumnName} LIKE @Tag
                ORDER BY {Configuration.PersistenceIdColumnName}, {Configuration.SequenceNrColumnName}";

            InsertEventSql = $@"
                INSERT INTO {Configuration.FullJournalTableName} (
                    {Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName},
                    {Configuration.TimestampColumnName},
                    {Configuration.IsDeletedColumnName},
                    {Configuration.ManifestColumnName},
                    {Configuration.PayloadColumnName},
                    {Configuration.TagsColumnName}
                ) VALUES (
                    @PersistenceId, 
                    @SequenceNr,
                    @Timestamp,
                    @IsDeleted,
                    @Manifest,
                    @Payload,
                    @Tag
                )";

            QueryEventsSql = $@"
                SELECT {allEventColumnNames}
                FROM {Configuration.FullJournalTableName} e
                WHERE ";
        }

        protected virtual string AllPersistenceIdsSql { get; }
        protected virtual string HighestSequenceNrSql { get; }
        protected virtual string DeleteBatchSql { get; }
        protected virtual string UpdateSequenceNrSql { get; }
        protected virtual string ByPersistenceIdSql { get; }
        protected virtual string ByTagSql { get; }
        protected virtual string InsertEventSql { get; }
        protected abstract string CreateEventsJournalSql { get; }
        protected abstract string CreateMetaTableSql { get; }

        [Obsolete]
        protected string QueryEventsSql { get; }

        protected abstract DbCommand CreateCommand(DbConnection connection);

        public virtual async Task<ImmutableArray<string>> SelectAllPersistenceIdsAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, AllPersistenceIdsSql))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                var builder = ImmutableArray.CreateBuilder<string>();
                while (await reader.ReadAsync(cancellationToken))
                {
                    builder.Add(reader.GetString(0));
                }

                return builder.ToImmutable();
            }
        }

        public virtual async Task SelectByPersistenceIdAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long fromSequenceNr, long toSequenceNr,
            long max, Action<IPersistentRepresentation> callback)
        {
            using (var command = GetCommand(connection, ByPersistenceIdSql))
            {
                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "@FromSequenceNr", DbType.Int64, fromSequenceNr);
                AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    var i = 0L;
                    while ((i++) < max && await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        callback(persistent);
                    }
                }
            }
        }

        public virtual async Task<long> SelectByTagAsync(DbConnection connection, CancellationToken cancellationToken, string tag, long fromOffset, long toOffset, long max,
            Action<ReplayedTaggedMessage> callback)
        {
            using (var command = GetCommand(connection, ByTagSql))
            {
                fromOffset = Math.Max(1, fromOffset);
                var take = Math.Min(toOffset - fromOffset, max);
                AddParameter(command, "@Tag", DbType.String, "%;" + tag + ";%");
                AddParameter(command, "@Skip", DbType.Int64, fromOffset - 1);
                AddParameter(command, "@Take", DbType.Int64, take);

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    var maxSequenceNr = 0L;
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        maxSequenceNr = Math.Max(maxSequenceNr, persistent.SequenceNr);
                        callback(new ReplayedTaggedMessage(persistent, tag, fromOffset));
                        fromOffset++;
                    }

                    return maxSequenceNr;
                }
            }
        }

        public virtual async Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId)
        {
            using (var command = GetCommand(connection, HighestSequenceNrSql))
            {
                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result is long ? Convert.ToInt64(result) : 0L;
            }
        }

        public virtual async Task InsertBatchAsync(DbConnection connection, CancellationToken cancellationToken, WriteJournalBatch write)
        {
            using (var command = GetCommand(connection, InsertEventSql))
            using (var tx = connection.BeginTransaction())
            {
                command.Transaction = tx;

                foreach (var entry in write.EntryTags)
                {
                    var evt = entry.Key;
                    var tags = entry.Value;

                    WriteEvent(command, evt, tags);
                    await command.ExecuteScalarAsync(cancellationToken);
                    command.Parameters.Clear();
                }

                tx.Commit();
            }
        }

        public virtual async Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long toSequenceNr)
        {
            using (var deleteCommand = GetCommand(connection, DeleteBatchSql))
            using (var highestSeqNrCommand = GetCommand(connection, HighestSequenceNrSql))
            {
                AddParameter(highestSeqNrCommand, "@PersistenceId", DbType.String, persistenceId);

                AddParameter(deleteCommand, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(deleteCommand, "@ToSequenceNr", DbType.Int64, toSequenceNr);

                using (var tx = connection.BeginTransaction())
                {
                    deleteCommand.Transaction = tx;
                    highestSeqNrCommand.Transaction = tx;

                    var res = await highestSeqNrCommand.ExecuteScalarAsync(cancellationToken);
                    var highestSeqNr = res is long ? Convert.ToInt64(res) : 0L;

                    await deleteCommand.ExecuteNonQueryAsync(cancellationToken);

                    if (highestSeqNr <= toSequenceNr)
                    {
                        using (var updateCommand = GetCommand(connection, UpdateSequenceNrSql))
                        {
                            updateCommand.Transaction = tx;

                            AddParameter(updateCommand, "@PersistenceId", DbType.String, persistenceId);
                            AddParameter(updateCommand, "@SequenceNr", DbType.Int64, highestSeqNr);

                            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
                            tx.Commit();
                        }
                    }
                    else tx.Commit();
                }
            }
        }

        public virtual async Task CreateTablesAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, CreateEventsJournalSql))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = CreateMetaTableSql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        protected DbCommand GetCommand(DbConnection connection, string sql)
        {
            var command = CreateCommand(connection);
            command.CommandText = sql;
            command.CommandTimeout = (int)Configuration.Timeout.TotalSeconds;
            return command;
        }

        protected virtual void WriteEvent(DbCommand command, IPersistentRepresentation e, IImmutableSet<string> tags)
        {
            var manifest = string.IsNullOrEmpty(e.Manifest) ? e.Payload.GetType().QualifiedTypeName() : e.Manifest;
            var serializer = Serialization.FindSerializerFor(e.Payload);
            var binary = serializer.ToBinary(e.Payload);

            AddParameter(command, "@PersistenceId", DbType.String, e.PersistenceId);
            AddParameter(command, "@SequenceNr", DbType.Int64, e.SequenceNr);
            AddParameter(command, "@Timestamp", DbType.Int64, TimestampProvider.GenerateTimestamp(e));
            AddParameter(command, "@IsDeleted", DbType.Boolean, false);
            AddParameter(command, "@Manifest", DbType.String, manifest);
            AddParameter(command, "@Payload", DbType.Binary, binary);

            if (tags.Count != 0)
            {
                var tagBuilder = new StringBuilder(";", tags.Sum(x => x.Length) + tags.Count + 1);
                foreach (var tag in tags)
                {
                    tagBuilder.Append(tag).Append(';');
                }

                AddParameter(command, "@Tag", DbType.String, tagBuilder.ToString());
            }
            else AddParameter(command, "@Tag", DbType.String, DBNull.Value);
        }

        protected virtual IPersistentRepresentation ReadEvent(DbDataReader reader)
        {
            var persistenceId = reader.GetString(PersistenceIdIndex);
            var sequenceNr = reader.GetInt64(SequenceNrIndex);
            var timestamp = reader.GetInt64(TimestampIndex);
            var isDeleted = reader.GetBoolean(IsDeletedIndex);
            var manifest = reader.GetString(ManifestIndex);
            var payload = reader[PayloadIndex];

            var type = Type.GetType(manifest, true);
            var deserializer = Serialization.FindSerializerForType(type);
            var deserialized = deserializer.FromBinary((byte[])payload, type);

            return new Persistent(deserialized, sequenceNr, persistenceId, manifest, isDeleted, ActorRefs.NoSender, null);
        }

        protected void AddParameter(DbCommand command, string parameterName, DbType parameterType, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = parameterName;
            parameter.DbType = parameterType;
            parameter.Value = value;

            command.Parameters.Add(parameter);
        }

        public async Task SelectEventsAsync(DbConnection connection, CancellationToken cancellationToken, IEnumerable<IHint> hints, Action<IPersistentRepresentation> callback)
        {
            using (var command = GetCommand(connection, QueryEventsSql))
            {
                command.CommandText += string.Join(" AND ", hints.Select(h => HintToSql(h, command)));

                using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        callback(persistent);
                    }
                }
            }
        }

        private string HintToSql(IHint hint, DbCommand command)
        {
            if (hint is TimestampRange)
            {
                var range = (TimestampRange)hint;
                var sb = new StringBuilder();

                if (range.From.HasValue)
                {
                    sb.Append(" e.").Append(Configuration.TimestampColumnName).Append(" >= @TimestampFrom ");
                    AddParameter(command, "@TimestampFrom", DbType.Int64, range.From.Value);
                }
                if (range.From.HasValue && range.To.HasValue) sb.Append("AND");
                if (range.To.HasValue)
                {
                    sb.Append(" e.").Append(Configuration.TimestampColumnName).Append(" < @TimestampTo ");
                    AddParameter(command, "@TimestampTo", DbType.Int64, range.To.Value);
                }

                return sb.ToString();
            }
            if (hint is PersistenceIdRange)
            {
                var range = (PersistenceIdRange)hint;
                var sb = new StringBuilder(" e.").Append(Configuration.PersistenceIdColumnName).Append(" IN (");
                var i = 0;
                foreach (var persistenceId in range.PersistenceIds)
                {
                    var paramName = "@Pid" + (i++);
                    sb.Append(paramName).Append(',');
                    AddParameter(command, paramName, DbType.String, persistenceId);
                }
                return range.PersistenceIds.Count == 0
                    ? string.Empty
                    : sb.Remove(sb.Length - 1, 1).Append(')').ToString();
            }
            else if (hint is WithManifest)
            {
                var manifest = (WithManifest)hint;
                AddParameter(command, "@Manifest", DbType.String, manifest.Manifest);
                return $" e.{Configuration.ManifestColumnName} = @Manifest";
            }
            else throw new NotSupportedException($"Sqlite journal doesn't support query with hint [{hint.GetType()}]");
        }
    }
}