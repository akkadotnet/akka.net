//-----------------------------------------------------------------------
// <copyright file="QuerySql.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Persistence.Embedded.Internal;
using Akka.Persistence.Embedded.Journal;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Query
{
    /// <summary>The rows of one read plus the highest ordering the same read transaction saw.</summary>
    internal sealed record QueryBatch(long MaxOrdering, List<RawJournalRow> Rows);

    /// <summary>The read journal's SQL. Runs on query pool threads.</summary>
    internal sealed class QuerySql
    {
        // Every statement is built once here from the table names, so a query only binds parameters.
        private readonly string _persistenceIdsFirstPage;
        private readonly string _persistenceIdsNextPage;
        private readonly string _byPersistenceId;
        private readonly string _maxOrdering;
        private readonly string _orderedAll;
        private readonly string _orderedByTag;
        private readonly string _fromEndAll;
        private readonly string _fromEndByTag;

        public QuerySql(QuerySettings settings)
        {
            var journal = settings.Journal.Tables.Journal;
            var tags = settings.Journal.Tables.TagTable;

            var selectRow = "SELECT j.ordering, j.created, j.deleted, j.persistence_id, j.sequence_number, j.message, j.manifest, j.identifier, j.writer_uuid, " +
                            $"(SELECT group_concat(t2.tag, char(31)) FROM {tags} t2 WHERE t2.ordering_id = j.ordering) AS tag_list FROM {journal} j";

            // Every statement skips deleted rows: a tombstone keeps its tag rows, so the tag queries filter on j.deleted too.
            var persistenceIds = $"SELECT DISTINCT persistence_id FROM {journal} WHERE deleted = 0";
            _persistenceIdsFirstPage = $"{persistenceIds} ORDER BY persistence_id LIMIT @n";
            _persistenceIdsNextPage = $"{persistenceIds} AND persistence_id > @after ORDER BY persistence_id LIMIT @n";

            _byPersistenceId =
                $"{selectRow} WHERE j.persistence_id = @persistence_id AND j.sequence_number >= @next " +
                "AND j.sequence_number <= @to AND j.deleted = 0 ORDER BY j.sequence_number LIMIT @n";

            _maxOrdering = $"SELECT MAX(ordering) FROM {journal}";

            _orderedAll = $"{selectRow} WHERE j.ordering > @offset AND j.ordering <= @max AND j.deleted = 0 ORDER BY j.ordering LIMIT @n";
            _fromEndAll = $"SELECT ordering FROM {journal} WHERE deleted = 0 ORDER BY ordering DESC LIMIT 1 OFFSET @skip";

            _orderedByTag = $"{selectRow} JOIN {tags} t ON t.ordering_id = j.ordering " +
                            "WHERE t.ordering_id > @offset AND t.ordering_id <= @max AND j.deleted = 0 AND t.tag = @tag " +
                            "ORDER BY j.ordering LIMIT @n";
            _fromEndByTag = $"SELECT j.ordering FROM {journal} j JOIN {tags} t ON t.ordering_id = j.ordering " +
                            "WHERE j.deleted = 0 AND t.tag = @tag ORDER BY j.ordering DESC LIMIT 1 OFFSET @skip";
        }

        private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private static List<RawJournalRow> ReadRows(SqliteCommand command)
        {
            var rows = new List<RawJournalRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(JournalSql.ReadRow(reader, hasTagList: true));
            return rows;
        }

        // ---- persistence ids -----------------------------------------------------------------------

        public List<string> ReadPersistenceIdsPage(SqliteConnection connection, string? after, int take)
        {
            using var command = Command(connection, null, after is null ? _persistenceIdsFirstPage : _persistenceIdsNextPage);
            if (after is not null)
                command.Parameters.Add("@after", SqliteType.Text).Value = after;
            command.Parameters.Add("@n", SqliteType.Integer).Value = (long)take;

            var ids = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                ids.Add(reader.GetString(0));
            return ids;
        }

        // ---- by persistence id ---------------------------------------------------------------------

        public List<RawJournalRow> ReadByPersistenceId(SqliteConnection connection, string persistenceId, long next, long to, int take)
        {
            using var command = Command(connection, null, _byPersistenceId);
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            command.Parameters.Add("@next", SqliteType.Integer).Value = next;
            command.Parameters.Add("@to", SqliteType.Integer).Value = to;
            command.Parameters.Add("@n", SqliteType.Integer).Value = (long)take;
            return ReadRows(command);
        }

        // ---- ordering based (by tag, all events) ---------------------------------------------------

        /// <summary>MAX(ordering) over the whole table. No deleted filter. 0 when empty.</summary>
        public long ReadMaxOrdering(SqliteConnection connection, SqliteTransaction? transaction = null)
        {
            using var command = Command(connection, transaction, _maxOrdering);
            var result = command.ExecuteScalar();
            return result is null or DBNull ? 0L : (long)result;
        }

        /// <summary>
        /// Reads one batch of events after <paramref name="offset"/> up to <paramref name="max"/>.
        /// <paramref name="tag"/> null = all events.
        /// </summary>
        public List<RawJournalRow> ReadOrdered(SqliteConnection connection, SqliteTransaction? transaction, string? tag, long offset, long max, int take)
        {
            using var command = Command(connection, transaction, tag is null ? _orderedAll : _orderedByTag);
            if (tag is not null)
                command.Parameters.Add("@tag", SqliteType.Text).Value = tag;

            command.Parameters.Add("@offset", SqliteType.Integer).Value = offset;
            command.Parameters.Add("@max", SqliteType.Integer).Value = max;
            command.Parameters.Add("@n", SqliteType.Integer).Value = (long)take;
            return ReadRows(command);
        }

        /// <summary>Reads MAX(ordering) and one batch in the same read transaction, so the batch cannot miss a committed row.</summary>
        public QueryBatch ReadMaxAndBatch(SqliteConnection connection, string? tag, long offset, int take)
        {
            using var transaction = connection.BeginTransaction(deferred: true);
            var max = ReadMaxOrdering(connection, transaction);
            var rows = ReadOrdered(connection, transaction, tag, offset, max, take);
            transaction.Commit();
            return new QueryBatch(max, rows);
        }

        /// <summary>The ordering at the <paramref name="count"/>-th position from the end, or null when there are fewer events.</summary>
        public long? ReadFromEndOrdering(SqliteConnection connection, string? tag, int count)
        {
            using var command = Command(connection, null, tag is null ? _fromEndAll : _fromEndByTag);
            if (tag is not null)
                command.Parameters.Add("@tag", SqliteType.Text).Value = tag;

            command.Parameters.Add("@skip", SqliteType.Integer).Value = (long)(count - 1);
            var result = command.ExecuteScalar();
            return result is null or DBNull ? null : (long)result;
        }
    }
}
