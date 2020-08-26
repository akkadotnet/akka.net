//-----------------------------------------------------------------------
// <copyright file="QueryExecutor.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
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
using Akka.Serialization;
using Akka.Util;

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
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="offset">TBD</param>
        /// <returns>TBD</returns>
        Task<ImmutableArray<string>> SelectAllPersistenceIdsAsync(DbConnection connection, CancellationToken cancellationToken, long offset);

        /// <summary>
        /// Asynchronously replays a <paramref name="callback"/> on all selected events for provided
        /// <paramref name="persistenceId"/>, within boundaries of <paramref name="fromSequenceNr"/>
        /// and <paramref name="toSequenceNr"/> up to <paramref name="max"/> number of events.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="callback">TBD</param>
        /// <returns>TBD</returns>
        Task SelectByPersistenceIdAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long fromSequenceNr, long toSequenceNr, long max, Action<IPersistentRepresentation> callback);

        /// <summary>
        /// Asynchronously replays <paramref name="callback"/> on all selected events, which have been tagged using
        /// provided <paramref name="tag"/>, within boundaries of <paramref name="fromOffset"/> and
        /// <paramref name="toOffset"/>, up to <paramref name="max"/> number of elements.
        /// Returns highest sequence number from selected events.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="tag">TBD</param>
        /// <param name="fromOffset">TBD</param>
        /// <param name="toOffset">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="callback">TBD</param>
        /// <returns>TBD</returns>
        Task<long> SelectByTagAsync(DbConnection connection, CancellationToken cancellationToken, string tag, long fromOffset, long toOffset, long max, Action<ReplayedTaggedMessage> callback);

        Task<long> SelectAllEventsAsync(
            DbConnection connection,
            CancellationToken cancellationToken, 
            long fromOffset, 
            long toOffset,
            long max, 
            Action<ReplayedEvent> callback);

        /// <summary>
        /// Asynchronously returns single number considered as the highest sequence number in current journal for the provided <paramref name="persistenceId"/>.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <returns>TBD</returns>
        Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId);

        Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken);

        /// <summary>
        /// Asynchronously inserts a collection of events and theirs tags into a journal table.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="write">TBD</param>
        /// <returns>TBD</returns>
        Task InsertBatchAsync(DbConnection connection, CancellationToken cancellationToken, WriteJournalBatch write);

        /// <summary>
        /// Asynchronously (permanently) deletes all events with given <paramref name="persistenceId"/>, up to provided <paramref name="toSequenceNr"/>.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
        Task DeleteBatchAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long toSequenceNr);

        /// <summary>
        /// Asynchronously tries to create new event journal and metadata tables.
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        Task CreateTablesAsync(DbConnection connection, CancellationToken cancellationToken);
    }

    /// <summary>
    /// TBD
    /// </summary>
    [Serializable]
    public class QueryConfiguration
    {
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string TagsColumnName;

        /// <summary>
        /// TBD
        /// </summary>
        public readonly string SchemaName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string JournalEventsTableName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string MetaTableName;
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
        public readonly string IsDeletedColumnName;
        /// <summary>
        /// TBD
        /// </summary>
        public readonly string OrderingColumnName;
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
        public string DefaultSerializer { get; }

        /// <summary>
        /// Uses the CommandBehavior.SequentialAccess when creating the command, providing a performance improvement for reading large BLOBS.
        /// </summary>
        public bool UseSequentialAccess { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="schemaName">TBD</param>
        /// <param name="journalEventsTableName">TBD</param>
        /// <param name="metaTableName">TBD</param>
        /// <param name="persistenceIdColumnName">TBD</param>
        /// <param name="sequenceNrColumnName">TBD</param>
        /// <param name="payloadColumnName">TBD</param>
        /// <param name="manifestColumnName">TBD</param>
        /// <param name="timestampColumnName">TBD</param>
        /// <param name="isDeletedColumnName">TBD</param>
        /// <param name="tagsColumnName">TBD</param>
        /// <param name="orderingColumnName">TBD</param>
        /// <param name="serializerIdColumnName">TBD</param>
        /// <param name="timeout">TBD</param>
        /// <param name="defaultSerializer">The default serializer used when not type override matching is found</param>
        /// <param name="useSequentialAccess">Uses the CommandBehavior.SequentialAccess when creating the command, providing a performance improvement for reading large BLOBS.</param>
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
            string orderingColumnName,
            string serializerIdColumnName,
            TimeSpan timeout,
            string defaultSerializer,
            bool useSequentialAccess)
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
            OrderingColumnName = orderingColumnName;
            DefaultSerializer = defaultSerializer;
            SerializerIdColumnName = serializerIdColumnName;
            UseSequentialAccess = useSequentialAccess;
        }

        /// <summary>
        /// TBD
        /// </summary>
        public string FullJournalTableName => string.IsNullOrEmpty(SchemaName) ? JournalEventsTableName : SchemaName + "." + JournalEventsTableName;
        /// <summary>
        /// TBD
        /// </summary>
        public string FullMetaTableName => string.IsNullOrEmpty(SchemaName) ? MetaTableName : SchemaName + "." + MetaTableName;
    }

    /// <summary>
    /// TBD
    /// </summary>
    public abstract class AbstractQueryExecutor : IJournalQueryExecutor
    {
        // indexes of particular fields returned from all events queries
        // they must match `allEventColumnNames` order
        /// <summary>
        /// TBD
        /// </summary>
        protected const int PersistenceIdIndex = 0;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int SequenceNrIndex = 1;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int TimestampIndex = 2;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int IsDeletedIndex = 3;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int ManifestIndex = 4;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int PayloadIndex = 5;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int SerializerIdIndex = 6;
        /// <summary>
        /// TBD
        /// </summary>
        protected const int OrderingIndex = 7;

        /// <summary>
        /// TBD
        /// </summary>
        protected static readonly string LongTypeName = typeof(long).FullName;

        /// <summary>
        /// TBD
        /// </summary>
        protected Akka.Serialization.Serialization Serialization;
        /// <summary>
        /// TBD
        /// </summary>
        protected ITimestampProvider TimestampProvider;
        /// <summary>
        /// TBD
        /// </summary>
        public QueryConfiguration Configuration { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="configuration">TBD</param>
        /// <param name="serialization">TBD</param>
        /// <param name="timestampProvider">TBD</param>
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
                e.{Configuration.PayloadColumnName} as Payload,
                e.{Configuration.SerializerIdColumnName} as SerializerId";

            AllPersistenceIdsSql = $@"
                SELECT DISTINCT u.Id as PersistenceId 
                FROM (
                    SELECT DISTINCT e.{Configuration.PersistenceIdColumnName} as Id 
                    FROM {Configuration.FullJournalTableName} e
                    WHERE e.{Configuration.OrderingColumnName} > @Ordering
                    UNION
                    SELECT DISTINCT e.{Configuration.PersistenceIdColumnName} as Id 
                    FROM {Configuration.FullMetaTableName} e
                ) as u";

            HighestSequenceNrSql = $@"
                SELECT MAX(u.SeqNr) as SequenceNr 
                FROM (
                    SELECT e.{Configuration.SequenceNrColumnName} as SeqNr FROM {Configuration.FullJournalTableName} e WHERE e.{Configuration.PersistenceIdColumnName} = @PersistenceId
                    UNION
                    SELECT m.{Configuration.SequenceNrColumnName} as SeqNr FROM {Configuration.FullMetaTableName} m WHERE m.{Configuration.PersistenceIdColumnName} = @PersistenceId) as u";

            DeleteBatchSql = $@"
                DELETE FROM {Configuration.FullJournalTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId AND {Configuration.SequenceNrColumnName} <= @ToSequenceNr;
                DELETE FROM {Configuration.FullMetaTableName}
                WHERE {Configuration.PersistenceIdColumnName} = @PersistenceId AND {Configuration.SequenceNrColumnName} <= @ToSequenceNr;";

            UpdateSequenceNrSql = $@"
                INSERT INTO {Configuration.FullMetaTableName} ({Configuration.PersistenceIdColumnName}, {Configuration.SequenceNrColumnName}) 
                VALUES (@PersistenceId, @SequenceNr);";

            ByPersistenceIdSql =
                $@"
                SELECT {allEventColumnNames}
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.PersistenceIdColumnName} = @PersistenceId
                AND e.{Configuration.SequenceNrColumnName} BETWEEN @FromSequenceNr AND @ToSequenceNr
                ORDER BY {Configuration.SequenceNrColumnName} ASC;";

            HighestTagOrderingSql =
                $@"
                SELECT MAX(e.{Configuration.OrderingColumnName}) as Ordering
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.OrderingColumnName} > @Ordering AND e.{Configuration.TagsColumnName} LIKE @Tag";

            ByTagSql =
                $@"
                SELECT {allEventColumnNames}, e.{Configuration.OrderingColumnName} as Ordering
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.OrderingColumnName} > @Ordering AND e.{Configuration.TagsColumnName} LIKE @Tag
                ORDER BY {Configuration.OrderingColumnName} ASC";

            AllEventsSql =
                $@"
                SELECT {allEventColumnNames}, e.{Configuration.OrderingColumnName} as Ordering
                FROM {Configuration.FullJournalTableName} e
                WHERE e.{Configuration.OrderingColumnName} > @Ordering
                ORDER BY {Configuration.OrderingColumnName} ASC";

            HighestOrderingSql =
                $@"
                SELECT MAX(e.{Configuration.OrderingColumnName}) as Ordering
                FROM {Configuration.FullJournalTableName} e";

            InsertEventSql = $@"
                INSERT INTO {Configuration.FullJournalTableName} (
                    {Configuration.PersistenceIdColumnName},
                    {Configuration.SequenceNrColumnName},
                    {Configuration.TimestampColumnName},
                    {Configuration.IsDeletedColumnName},
                    {Configuration.ManifestColumnName},
                    {Configuration.PayloadColumnName},
                    {Configuration.TagsColumnName},
                    {Configuration.SerializerIdColumnName}
                ) VALUES (
                    @PersistenceId, 
                    @SequenceNr,
                    @Timestamp,
                    @IsDeleted,
                    @Manifest,
                    @Payload,
                    @Tag,
                    @SerializerId
                )";

            QueryEventsSql = $@"
                SELECT {allEventColumnNames}
                FROM {Configuration.FullJournalTableName} e
                WHERE ";
        }

        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string AllPersistenceIdsSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string HighestSequenceNrSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string DeleteBatchSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string UpdateSequenceNrSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string ByPersistenceIdSql { get; }
        /// <summary>
        /// Query to return the highest ordering number for a tag
        /// </summary>
        protected virtual string HighestTagOrderingSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string ByTagSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string AllEventsSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string HighestOrderingSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected virtual string InsertEventSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected abstract string CreateEventsJournalSql { get; }
        /// <summary>
        /// TBD
        /// </summary>
        protected abstract string CreateMetaTableSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        [Obsolete]
        protected string QueryEventsSql { get; }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <returns>TBD</returns>
        protected abstract DbCommand CreateCommand(DbConnection connection);

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="offset">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task<ImmutableArray<string>> SelectAllPersistenceIdsAsync(DbConnection connection, CancellationToken cancellationToken, long offset)
        {
            using (var command = GetCommand(connection, AllPersistenceIdsSql))
            {
                AddParameter(command, "@Ordering", DbType.Int64, offset);

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
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="fromSequenceNr">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="callback">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task SelectByPersistenceIdAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId, long fromSequenceNr, long toSequenceNr,
            long max, Action<IPersistentRepresentation> callback)
        {
            using (var command = GetCommand(connection, ByPersistenceIdSql))
            {
                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);
                AddParameter(command, "@FromSequenceNr", DbType.Int64, fromSequenceNr);
                AddParameter(command, "@ToSequenceNr", DbType.Int64, toSequenceNr);

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
                    var i = 0L;
                    while ((i++) < max && await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        callback(persistent);
                    }
                }
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="tag">TBD</param>
        /// <param name="fromOffset">TBD</param>
        /// <param name="toOffset">TBD</param>
        /// <param name="max">TBD</param>
        /// <param name="callback">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task<long> SelectByTagAsync(DbConnection connection, CancellationToken cancellationToken, string tag, long fromOffset, long toOffset, long max,
            Action<ReplayedTaggedMessage> callback)
        {
            using (var command = GetCommand(connection, ByTagSql))
            {
                var take = Math.Min(toOffset - fromOffset, max);
                AddParameter(command, "@Tag", DbType.String, "%;" + tag + ";%");
                AddParameter(command, "@Ordering", DbType.Int64, fromOffset);
                AddParameter(command, "@Take", DbType.Int64, take);

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
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        var ordering = reader.GetInt64(OrderingIndex);
                        callback(new ReplayedTaggedMessage(persistent, tag, ordering));
                    }
                }
            }

            using (var command = GetCommand(connection, HighestTagOrderingSql))
            {
                AddParameter(command, "@Tag", DbType.String, "%;" + tag + ";%");
                AddParameter(command, "@Ordering", DbType.Int64, fromOffset);
                var maxOrdering = (await command.ExecuteScalarAsync(cancellationToken)) as long? ?? 0L;
                return maxOrdering;
            }
        }

        public virtual async Task<long> SelectAllEventsAsync(
            DbConnection connection,
            CancellationToken cancellationToken, 
            long fromOffset,
            long toOffset,
            long max, 
            Action<ReplayedEvent> callback)
        {
            long maxOrdering;
            using (var command = GetCommand(connection, HighestOrderingSql))
            {
                maxOrdering = (await command.ExecuteScalarAsync(cancellationToken)) as long? ?? 0L;
            }

            using (var command = GetCommand(connection, AllEventsSql))
            {
                var take = Math.Min(toOffset - fromOffset, max);

                AddParameter(command, "@Ordering", DbType.Int64, fromOffset);
                AddParameter(command, "@Take", DbType.Int64, take);

                var commandBehavior = Configuration.UseSequentialAccess ? 
                    CommandBehavior.SequentialAccess : 
                    CommandBehavior.Default;

                using (var reader = await command.ExecuteReaderAsync(commandBehavior, cancellationToken))
                {
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        var persistent = ReadEvent(reader);
                        var ordering = reader.GetInt64(OrderingIndex);
                        callback(new ReplayedEvent(persistent, ordering));
                    }
                }
            }

            return maxOrdering;
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken, string persistenceId)
        {
            using (var command = GetCommand(connection, HighestSequenceNrSql))
            {
                AddParameter(command, "@PersistenceId", DbType.String, persistenceId);

                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result is long ? Convert.ToInt64(result) : 0L;
            }
        }

        public virtual async Task<long> SelectHighestSequenceNrAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, HighestOrderingSql))
            {
                var result = await command.ExecuteScalarAsync(cancellationToken);
                return result is long ? Convert.ToInt64(result) : 0L;
            }
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="write">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <param name="persistenceId">TBD</param>
        /// <param name="toSequenceNr">TBD</param>
        /// <returns>TBD</returns>
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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="connection">TBD</param>
        /// <param name="cancellationToken">TBD</param>
        /// <returns>TBD</returns>
        public virtual async Task CreateTablesAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            using (var command = GetCommand(connection, CreateEventsJournalSql))
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
                command.CommandText = CreateMetaTableSql;
                await command.ExecuteNonQueryAsync(cancellationToken);
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
        /// <param name="e">TBD</param>
        /// <param name="tags">TBD</param>
        /// <returns>TBD</returns>
        protected virtual void WriteEvent(DbCommand command, IPersistentRepresentation e, IImmutableSet<string> tags)
        {
            var payloadType = e.Payload.GetType();
            var serializer = Serialization.FindSerializerForType(payloadType, Configuration.DefaultSerializer);

            // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
            string manifest = "";
            var binary = Akka.Serialization.Serialization.WithTransport(Serialization.System, () =>
            {
                
                if (serializer is SerializerWithStringManifest stringManifest)
                {
                    manifest = stringManifest.Manifest(e.Payload);
                }
                else
                {
                    if (serializer.IncludeManifest)
                    {
                        manifest = e.Payload.GetType().TypeQualifiedName();
                    }
                }

                return serializer.ToBinary(e.Payload);
            });
           

            AddParameter(command, "@PersistenceId", DbType.String, e.PersistenceId);
            AddParameter(command, "@SequenceNr", DbType.Int64, e.SequenceNr);
            AddParameter(command, "@Timestamp", DbType.Int64, TimestampProvider.GenerateTimestamp(e));
            AddParameter(command, "@IsDeleted", DbType.Boolean, false);
            AddParameter(command, "@Manifest", DbType.String, manifest);
            AddParameter(command, "@Payload", DbType.Binary, binary);
            AddParameter(command, "@SerializerId", DbType.Int32, serializer.Identifier);

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

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="reader">TBD</param>
        /// <returns>TBD</returns>
        protected virtual IPersistentRepresentation ReadEvent(DbDataReader reader)
        {
            var persistenceId = reader.GetString(PersistenceIdIndex);
            var sequenceNr = reader.GetInt64(SequenceNrIndex);
            var timestamp = reader.GetInt64(TimestampIndex);
            var isDeleted = reader.GetBoolean(IsDeletedIndex);
            var manifest = reader.GetString(ManifestIndex);
            var payload = reader[PayloadIndex];

            object deserialized;
            if (reader.IsDBNull(SerializerIdIndex))
            {
                // Support old writes that did not set the serializer id
                var type = Type.GetType(manifest, true);
                var deserializer = Serialization.FindSerializerForType(type, Configuration.DefaultSerializer);
                // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                deserialized = Akka.Serialization.Serialization.WithTransport(Serialization.System, () => deserializer.FromBinary((byte[])payload, type) );
            }
            else
            {
                var serializerId = reader.GetInt32(SerializerIdIndex);
                // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                deserialized = Serialization.Deserialize((byte[])payload, serializerId, manifest);
            }

            return new Persistent(deserialized, sequenceNr, persistenceId, manifest, isDeleted, ActorRefs.NoSender, null);
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
    }
}
