//-----------------------------------------------------------------------
// <copyright file="QuerySql.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
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
        private readonly QuerySettings _settings;
        private readonly JournalTableNames _t;

        // Every statement is built once here from the settings, so a query only binds parameters.
        private readonly string _persistenceIdsFirstPage;
        private readonly string _persistenceIdsNextPage;
        private readonly string _byPersistenceId;
        private readonly string _maxOrdering;
        private readonly string _orderedAll;
        private readonly string _orderedByTag;
        private readonly string _fromEndAll;
        private readonly string _fromEndByTag;
        private readonly string _orderings;

        public QuerySql(QuerySettings settings)
        {
            _settings = settings;
            _t = settings.Journal.Tables;

            var tagList = settings.TagReadMode == TagReadMode.TagTable
                ? $"(SELECT group_concat(t2.{_t.TagValue}, char(31)) FROM {_t.TagTable} t2 WHERE t2.{_t.TagOrderingId} = j.{_t.Ordering}) AS tag_list"
                : $"j.{_t.Tags} AS tag_list";
            var selectRow = $"SELECT j.{_t.Ordering}, j.{_t.Created}, j.{_t.Deleted}, j.{_t.PersistenceId}, j.{_t.SequenceNumber}, " +
                         $"j.{_t.Message}, j.{_t.Manifest}, j.{_t.Identifier}"
                         + (settings.Journal.UseWriterUuid ? $", j.{_t.WriterUuid}" : "")
                         + $", {tagList} FROM {_t.Journal} j";

            var persistenceIds = $"SELECT DISTINCT {_t.PersistenceId} FROM {_t.Journal} WHERE {_t.Deleted} = 0";
            _persistenceIdsFirstPage = $"{persistenceIds} ORDER BY {_t.PersistenceId} LIMIT @n";
            _persistenceIdsNextPage = $"{persistenceIds} AND {_t.PersistenceId} > @after ORDER BY {_t.PersistenceId} LIMIT @n";

            _byPersistenceId =
                $"{selectRow} WHERE j.{_t.PersistenceId} = @persistence_id AND j.{_t.SequenceNumber} >= @next " +
                $"AND j.{_t.SequenceNumber} <= @to AND j.{_t.Deleted} = 0 ORDER BY j.{_t.SequenceNumber} LIMIT @n";

            _maxOrdering = $"SELECT MAX({_t.Ordering}) FROM {_t.Journal}";

            _orderedAll = $"{selectRow} WHERE j.{_t.Ordering} > @offset AND j.{_t.Ordering} <= @max AND j.{_t.Deleted} = 0 " +
                          $"ORDER BY j.{_t.Ordering} LIMIT @n";
            _fromEndAll = $"SELECT {_t.Ordering} FROM {_t.Journal} WHERE {_t.Deleted} = 0 ORDER BY {_t.Ordering} DESC LIMIT 1 OFFSET @skip";

            if (UseTagTable)
            {
                _orderedByTag = $"{selectRow} JOIN {_t.TagTable} t ON t.{_t.TagOrderingId} = j.{_t.Ordering} " +
                                $"WHERE t.{_t.TagOrderingId} > @offset AND t.{_t.TagOrderingId} <= @max AND j.{_t.Deleted} = 0 AND t.{_t.TagValue} = @tag " +
                                $"ORDER BY j.{_t.Ordering} LIMIT @n";
                _fromEndByTag = $"SELECT j.{_t.Ordering} FROM {_t.Journal} j JOIN {_t.TagTable} t ON t.{_t.TagOrderingId} = j.{_t.Ordering} " +
                                $"WHERE j.{_t.Deleted} = 0 AND t.{_t.TagValue} = @tag ORDER BY j.{_t.Ordering} DESC LIMIT 1 OFFSET @skip";
            }
            else
            {
                _orderedByTag = $"{selectRow} WHERE j.{_t.Tags} IS NOT NULL AND j.{_t.Tags} LIKE @pattern ESCAPE '~' AND j.{_t.Deleted} = 0 " +
                                $"AND j.{_t.Ordering} > @offset AND j.{_t.Ordering} <= @max ORDER BY j.{_t.Ordering} LIMIT @n";
                _fromEndByTag = $"SELECT {_t.Ordering} FROM {_t.Journal} WHERE {_t.Tags} IS NOT NULL AND {_t.Tags} LIKE @pattern ESCAPE '~' " +
                                $"AND {_t.Deleted} = 0 ORDER BY {_t.Ordering} DESC LIMIT 1 OFFSET @skip";
            }

            _orderings = $"SELECT {_t.Ordering} FROM {_t.Journal} WHERE {_t.Ordering} > @offset ORDER BY {_t.Ordering} LIMIT @take";
        }

        private bool UseTagTable => _settings.TagReadMode == TagReadMode.TagTable;

        /// <summary>The LIKE pattern that matches a tag in the Csv column. SQLite's LIKE is ASCII case-insensitive.</summary>
        public string CsvPattern(string tag)
        {
            var sb = new StringBuilder("%");
            foreach (var c in _settings.Journal.TagSeparator + tag + _settings.Journal.TagSeparator)
            {
                if (c is '~' or '%' or '_')
                    sb.Append('~');
                sb.Append(c);
            }

            return sb.Append('%').ToString();
        }

        private SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            return command;
        }

        private List<RawJournalRow> ReadRows(SqliteCommand command)
        {
            var rows = new List<RawJournalRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(JournalSql.ReadRow(reader, _settings.Journal.UseWriterUuid, hasTagList: true));
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
            {
                if (UseTagTable)
                    command.Parameters.Add("@tag", SqliteType.Text).Value = tag;
                else
                    command.Parameters.Add("@pattern", SqliteType.Text).Value = CsvPattern(tag);
            }

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
            {
                if (UseTagTable)
                    command.Parameters.Add("@tag", SqliteType.Text).Value = tag;
                else
                    command.Parameters.Add("@pattern", SqliteType.Text).Value = CsvPattern(tag);
            }

            command.Parameters.Add("@skip", SqliteType.Integer).Value = (long)(count - 1);
            var result = command.ExecuteScalar();
            return result is null or DBNull ? null : (long)result;
        }

        // ---- gap tracking --------------------------------------------------------------------------

        public List<long> ReadOrderings(SqliteConnection connection, long offset, int take)
        {
            using var command = Command(connection, null, _orderings);
            command.Parameters.Add("@offset", SqliteType.Integer).Value = offset;
            command.Parameters.Add("@take", SqliteType.Integer).Value = (long)take;

            var orderings = new List<long>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                orderings.Add(reader.GetInt64(0));
            return orderings;
        }
    }
}
