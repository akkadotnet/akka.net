//-----------------------------------------------------------------------
// <copyright file="JournalSql.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Collections.Generic;
using Akka.Persistence.Embedded.Internal;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Journal
{
    /// <summary>The journal's SQL. Table names are validated identifiers, so they go in unquoted.</summary>
    internal sealed class JournalSql
    {
        private readonly string _highestSequenceNr;
        private readonly string _highestSequenceNrFrom;

        public JournalSql(JournalSettings settings)
        {
            var journal = settings.Tables.Journal;
            var tags = settings.Tables.TagTable;

            Insert = $"INSERT INTO {journal} (created, deleted, persistence_id, sequence_number, message, manifest, identifier, writer_uuid) " +
                     "VALUES (@created, @deleted, @persistence_id, @sequence_number, @message, @manifest, @identifier, @writer_uuid)";
            InsertReturningId = Insert + "; SELECT last_insert_rowid();";
            InsertTag = $"INSERT INTO {tags} (ordering_id, tag, sequence_nr, persistence_id) " +
                        "VALUES (@ordering_id, @tag, @sequence_nr, @persistence_id)";

            Replay = $"SELECT ordering, created, deleted, persistence_id, sequence_number, message, manifest, identifier, writer_uuid FROM {journal} " +
                     "WHERE persistence_id = @persistence_id AND sequence_number >= @from AND sequence_number <= @to AND deleted = 0 " +
                     "ORDER BY sequence_number LIMIT @take";

            // The highest row at or below the target is the tombstone: it stays, marked deleted and with an empty message
            // (the highest sequence number survives, the payload does not). Lower rows and their tag rows go.
            DeleteSelectHighest = $"SELECT sequence_number FROM {journal} " +
                                  "WHERE persistence_id = @persistence_id AND sequence_number <= @to ORDER BY sequence_number DESC LIMIT 1";
            DeleteTombstone = $"UPDATE {journal} SET deleted = 1, message = x'' WHERE persistence_id = @persistence_id AND sequence_number = @marker";
            DeletePhysical = $"DELETE FROM {journal} WHERE persistence_id = @persistence_id AND sequence_number < @marker";
            DeleteTags = $"DELETE FROM {tags} WHERE sequence_nr < @marker AND persistence_id = @persistence_id";

            // MAX over every row, deleted ones included: the tombstone keeps the highest sequence number alive
            _highestSequenceNr = $"SELECT MAX(sequence_number) FROM {journal} WHERE persistence_id = @persistence_id";
            _highestSequenceNrFrom = _highestSequenceNr + " AND sequence_number > @from";
        }

        public string Insert { get; }
        public string InsertReturningId { get; }
        public string InsertTag { get; }
        public string Replay { get; }
        public string DeleteSelectHighest { get; }
        public string DeleteTombstone { get; }
        public string DeletePhysical { get; }
        public string DeleteTags { get; }

        // ---- execution helpers (run on worker threads) ---------------------------------------------

        /// <summary>Reads the highest sequence number of a persistence id. 0 when there is none.</summary>
        public long ReadHighestSequenceNr(SqliteConnection connection, string persistenceId, long from)
        {
            using var command = connection.CreateCommand();
            command.CommandText = from != 0 ? _highestSequenceNrFrom : _highestSequenceNr;
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            if (from != 0)
                command.Parameters.Add("@from", SqliteType.Integer).Value = from;

            var result = command.ExecuteScalar();
            return result is null or System.DBNull ? 0L : (long)result;
        }

        /// <summary>Reads one replay batch.</summary>
        public List<RawJournalRow> ReadReplayBatch(SqliteConnection connection, string persistenceId, long from, long to, int take)
        {
            using var command = connection.CreateCommand();
            command.CommandText = Replay;
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            command.Parameters.Add("@from", SqliteType.Integer).Value = from;
            command.Parameters.Add("@to", SqliteType.Integer).Value = to;
            command.Parameters.Add("@take", SqliteType.Integer).Value = (long)take;

            var rows = new List<RawJournalRow>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
                rows.Add(ReadRow(reader, hasTagList: false));
            return rows;
        }

        /// <summary>
        /// Reads a row laid out as ordering, created, deleted, persistence_id, sequence_number, message, manifest,
        /// identifier, writer_uuid, [tag list].
        /// </summary>
        public static RawJournalRow ReadRow(SqliteDataReader reader, bool hasTagList)
        {
            return new RawJournalRow
            {
                Ordering = reader.GetInt64(0),
                Created = reader.GetInt64(1),
                Deleted = reader.GetInt64(2),
                PersistenceId = reader.GetString(3),
                SequenceNr = reader.GetInt64(4),
                Message = reader.IsDBNull(5) ? System.Array.Empty<byte>() : (byte[])reader.GetValue(5),
                Manifest = reader.IsDBNull(6) ? null : reader.GetString(6),
                Identifier = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                WriterUuid = reader.IsDBNull(8) ? null : reader.GetString(8),
                TagList = hasTagList && !reader.IsDBNull(9) ? reader.GetString(9) : null
            };
        }
    }
}
