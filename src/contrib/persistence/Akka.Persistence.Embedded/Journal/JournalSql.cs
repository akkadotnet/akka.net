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
    /// <summary>The journal's SQL text. Table and column names are validated identifiers, so they go in unquoted.</summary>
    internal sealed class JournalSql
    {
        private readonly JournalSettings _settings;

        public JournalSql(JournalSettings settings)
        {
            _settings = settings;
            var t = settings.Tables;

            var columns = $"{t.Created}, {t.Deleted}, {t.PersistenceId}, {t.SequenceNumber}, {t.Message}"
                + (settings.WritesTagsColumn ? $", {t.Tags}" : "")
                + $", {t.Manifest}, {t.Identifier}"
                + (settings.UseWriterUuid ? $", {t.WriterUuid}" : "");
            var values = "@created, @deleted, @persistence_id, @sequence_number, @message"
                + (settings.WritesTagsColumn ? ", @tags" : "")
                + ", @manifest, @identifier"
                + (settings.UseWriterUuid ? ", @writer_uuid" : "");
            Insert = $"INSERT INTO {t.Journal} ({columns}) VALUES ({values})";
            InsertReturningId = Insert + "; SELECT last_insert_rowid();";
            InsertTag = $"INSERT INTO {t.TagTable} ({t.TagOrderingId}, {t.TagValue}, {t.TagSequenceNr}, {t.TagPersistenceId}) " +
                        "VALUES (@ordering_id, @tag, @sequence_nr, @persistence_id)";

            var replayColumns = $"{t.Ordering}, {t.Created}, {t.Deleted}, {t.PersistenceId}, {t.SequenceNumber}, {t.Message}, {t.Manifest}, {t.Identifier}"
                + (settings.UseWriterUuid ? $", {t.WriterUuid}" : "");
            Replay = $"SELECT {replayColumns} FROM {t.Journal} " +
                     $"WHERE {t.PersistenceId} = @persistence_id AND {t.SequenceNumber} >= @from AND {t.SequenceNumber} <= @to AND {t.Deleted} = 0 " +
                     $"ORDER BY {t.SequenceNumber} LIMIT @take";

            DeleteSelectHighest = $"SELECT {t.SequenceNumber} FROM {t.Journal} " +
                                  $"WHERE {t.PersistenceId} = @persistence_id AND {t.SequenceNumber} <= @to " +
                                  $"ORDER BY {t.SequenceNumber} DESC LIMIT 1";
            DeleteTombstone = $"UPDATE {t.Journal} SET {t.Deleted} = 1 WHERE {t.PersistenceId} = @persistence_id AND {t.SequenceNumber} = @marker";
            DeletePhysical = $"DELETE FROM {t.Journal} WHERE {t.PersistenceId} = @persistence_id AND {t.SequenceNumber} < @marker";
            DeleteTags = $"DELETE FROM {t.TagTable} WHERE {t.TagSequenceNr} < @marker AND {t.TagPersistenceId} = @persistence_id";
            MetadataInsert = $"INSERT INTO {t.Metadata} ({t.MetadataPersistenceId}, {t.MetadataSequenceNumber}) " +
                             $"SELECT @persistence_id, @marker WHERE NOT EXISTS " +
                             $"(SELECT 1 FROM {t.Metadata} WHERE {t.MetadataPersistenceId} = @persistence_id AND {t.MetadataSequenceNumber} = @marker)";
            MetadataDelete = $"DELETE FROM {t.Metadata} WHERE {t.MetadataPersistenceId} = @persistence_id AND {t.MetadataSequenceNumber} < @marker";
        }

        public string Insert { get; }
        public string InsertReturningId { get; }
        public string InsertTag { get; }
        public string Replay { get; }
        public string DeleteSelectHighest { get; }
        public string DeleteTombstone { get; }
        public string DeletePhysical { get; }
        public string DeleteTags { get; }
        public string MetadataInsert { get; }
        public string MetadataDelete { get; }

        public string HighestSequenceNr(bool hasFrom)
        {
            var t = _settings.Tables;
            var journalFrom = hasFrom ? $" AND {t.SequenceNumber} > @from" : "";
            var journal = $"SELECT MAX({t.SequenceNumber}) FROM {t.Journal} WHERE {t.PersistenceId} = @persistence_id{journalFrom}";
            if (!_settings.DeleteCompatibilityMode)
                return journal;

            var metadataFrom = hasFrom ? $" AND {t.MetadataSequenceNumber} > @from" : "";
            return "SELECT MAX(c1) FROM (" +
                   $"SELECT MAX({t.SequenceNumber}) AS c1 FROM {t.Journal} WHERE {t.PersistenceId} = @persistence_id{journalFrom} " +
                   "UNION " +
                   $"SELECT MAX({t.MetadataSequenceNumber}) AS c1 FROM {t.Metadata} WHERE {t.MetadataPersistenceId} = @persistence_id{metadataFrom})";
        }

        // ---- execution helpers (run on worker threads) ---------------------------------------------

        /// <summary>Reads the highest sequence number of a persistence id. 0 when there is none.</summary>
        public long ReadHighestSequenceNr(SqliteConnection connection, string persistenceId, long from)
        {
            using var command = connection.CreateCommand();
            command.CommandText = HighestSequenceNr(from != 0);
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
                rows.Add(ReadRow(reader, _settings.UseWriterUuid, hasTagList: false));
            return rows;
        }

        /// <summary>
        /// Reads a row laid out as ordering, created, deleted, persistence_id, sequence_number, message, manifest,
        /// identifier, [writer_uuid], [tag list].
        /// </summary>
        public static RawJournalRow ReadRow(SqliteDataReader reader, bool hasWriterUuid, bool hasTagList)
        {
            var next = 8;
            string? writerUuid = null;
            if (hasWriterUuid)
            {
                writerUuid = reader.IsDBNull(next) ? null : reader.GetString(next);
                next++;
            }

            string? tagList = null;
            if (hasTagList)
                tagList = reader.IsDBNull(next) ? null : reader.GetString(next);

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
                WriterUuid = writerUuid,
                TagList = tagList
            };
        }
    }
}
