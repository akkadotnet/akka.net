//-----------------------------------------------------------------------
// <copyright file="SnapshotSql.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Persistence.Embedded.Internal;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Snapshot
{
    /// <summary>A snapshot row as read from SQLite, before deserialization.</summary>
    internal sealed class RawSnapshotRow
    {
        public string PersistenceId { get; init; } = "";
        public long SequenceNr { get; init; }
        public long Created { get; init; }
        public byte[] Payload { get; init; } = Array.Empty<byte>();
        public string? Manifest { get; init; }
        public long? SerializerId { get; init; }
    }

    /// <summary>The snapshot store's SQL. Runs on the store's single worker thread.</summary>
    internal sealed class SnapshotSql
    {
        private readonly SnapshotTableNames _t;

        public SnapshotSql(SnapshotSettings settings)
        {
            _t = settings.Tables;
        }

        public void Save(SqliteConnection connection, string persistenceId, long sequenceNr, long created, byte[] payload, string manifest, int serializerId)
        {
            using var transaction = connection.BeginTransaction(deferred: false);

            // Same as linq2db's InsertOrReplace: update first, insert when nothing matched. Works without a primary key.
            int changed;
            using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText =
                    $"UPDATE {_t.Snapshot} SET {_t.Created} = @created, {_t.Payload} = @snapshot, {_t.Manifest} = @manifest, {_t.SerializerId} = @serializer_id " +
                    $"WHERE {_t.PersistenceId} = @persistence_id AND {_t.SequenceNumber} = @sequence_number";
                Bind(update, persistenceId, sequenceNr, created, payload, manifest, serializerId);
                changed = update.ExecuteNonQuery();
            }

            if (changed == 0)
            {
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText =
                    $"INSERT INTO {_t.Snapshot} ({_t.PersistenceId}, {_t.SequenceNumber}, {_t.Created}, {_t.Payload}, {_t.Manifest}, {_t.SerializerId}) " +
                    "VALUES (@persistence_id, @sequence_number, @created, @snapshot, @manifest, @serializer_id)";
                Bind(insert, persistenceId, sequenceNr, created, payload, manifest, serializerId);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        private static void Bind(SqliteCommand command, string persistenceId, long sequenceNr, long created, byte[] payload, string manifest, int serializerId)
        {
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            command.Parameters.Add("@sequence_number", SqliteType.Integer).Value = sequenceNr;
            command.Parameters.Add("@created", SqliteType.Integer).Value = created;
            command.Parameters.Add("@snapshot", SqliteType.Blob).Value = payload;
            command.Parameters.Add("@manifest", SqliteType.Text).Value = manifest;
            command.Parameters.Add("@serializer_id", SqliteType.Integer).Value = (long)serializerId;
        }

        public RawSnapshotRow? Load(SqliteConnection connection, string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {_t.PersistenceId}, {_t.SequenceNumber}, {_t.Created}, {_t.Payload}, {_t.Manifest}, {_t.SerializerId} " +
                $"FROM {_t.Snapshot} WHERE {Where(command, persistenceId, criteria)} ORDER BY {_t.SequenceNumber} DESC LIMIT 1";

            using var reader = command.ExecuteReader();
            if (!reader.Read())
                return null;

            return new RawSnapshotRow
            {
                PersistenceId = reader.GetString(0),
                SequenceNr = reader.GetInt64(1),
                Created = reader.GetInt64(2),
                Payload = reader.IsDBNull(3) ? Array.Empty<byte>() : (byte[])reader.GetValue(3),
                Manifest = reader.IsDBNull(4) ? null : reader.GetString(4),
                SerializerId = reader.IsDBNull(5) ? null : reader.GetInt64(5)
            };
        }

        public void Delete(SqliteConnection connection, SnapshotMetadata metadata)
        {
            using var command = connection.CreateCommand();
            var sql = $"DELETE FROM {_t.Snapshot} WHERE {_t.PersistenceId} = @persistence_id AND {_t.SequenceNumber} = @sequence_number";
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = metadata.PersistenceId;
            command.Parameters.Add("@sequence_number", SqliteType.Integer).Value = metadata.SequenceNr;
            if (metadata.Timestamp > DateTime.MinValue)
            {
                sql += $" AND {_t.Created} <= @ticks";
                command.Parameters.Add("@ticks", SqliteType.Integer).Value = metadata.Timestamp.Ticks;
            }

            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public void Delete(SqliteConnection connection, string persistenceId, SnapshotSelectionCriteria criteria)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {_t.Snapshot} WHERE {Where(command, persistenceId, criteria)}";
            command.ExecuteNonQuery();
        }

        /// <summary>
        /// The same four-way dispatch Akka.Persistence.Sql uses. MinSequenceNr and MinTimestamp are ignored,
        /// like they are there.
        /// </summary>
        private string Where(SqliteCommand command, string persistenceId, SnapshotSelectionCriteria criteria)
        {
            command.Parameters.Add("@persistence_id", SqliteType.Text).Value = persistenceId;
            var where = $"{_t.PersistenceId} = @persistence_id";

            if (criteria.MaxSequenceNr != long.MaxValue)
            {
                where += $" AND {_t.SequenceNumber} <= @sequence_number";
                command.Parameters.Add("@sequence_number", SqliteType.Integer).Value = criteria.MaxSequenceNr;
            }

            if (criteria.MaxTimeStamp != DateTime.MaxValue)
            {
                where += $" AND {_t.Created} <= @ticks";
                command.Parameters.Add("@ticks", SqliteType.Integer).Value = criteria.MaxTimeStamp.Ticks;
            }

            return where;
        }
    }
}
