//-----------------------------------------------------------------------
// <copyright file="SqliteSchema.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>Thrown when the database does not have the tables or columns the plugin needs.</summary>
    internal sealed class SqliteSchemaException : Exception
    {
        public SqliteSchemaException(string message) : base(message)
        {
        }
    }

    /// <summary>
    /// Builds the exact DDL text Akka.Persistence.Sql (linq2db) sends to SQLite, creates missing tables and
    /// verifies that the tables have what the plugin needs. Never alters or drops anything.
    /// </summary>
    internal static class SqliteSchema
    {
        private const string StatementEnd = ";\r\n";

        private readonly record struct Column(string Name, string Type, bool NotNull, string Suffix = "");

        // ---- DDL text ------------------------------------------------------------------------------

        /// <summary>J1, J2 and J3: the journal table plus its three indexes.</summary>
        public static string JournalDdl(JournalSettings settings)
        {
            var t = settings.Tables;
            var columns = new List<Column>
            {
                new(t.Ordering, "INTEGER", true, " PRIMARY KEY AUTOINCREMENT"),
                new(t.Created, "BigInt", true),
                new(t.Deleted, "Bit", true),
                new(t.PersistenceId, "NVarChar(255)", true),
                new(t.SequenceNumber, "BigInt", true),
                new(t.Message, "VarBinary", true)
            };
            if (settings.WritesTagsColumn)
                columns.Add(new Column(t.Tags, "NVarChar(100)", false));
            columns.Add(new Column(t.Manifest, "NVarChar(500)", false));
            columns.Add(new Column(t.Identifier, "INTEGER", false));
            if (settings.UseWriterUuid)
                columns.Add(new Column(t.WriterUuid, "NVarChar(128)", false));

            var sb = new StringBuilder();
            AppendCreateTable(sb, t.Journal, columns, typePadding: 2, constraint: null);
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE UNIQUE INDEX IF NOT EXISTS {t.Journal}_uq ON {t.Journal} ({t.PersistenceId}, {t.SequenceNumber})").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.Journal}_{t.Created}_idx ON {t.Journal} ({t.Created})").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.Journal}_{t.SequenceNumber}_idx ON {t.Journal} ({t.SequenceNumber});");
            return sb.ToString();
        }

        /// <summary>T1: the tag table plus its two indexes.</summary>
        public static string TagTableDdl(JournalSettings settings)
        {
            var t = settings.Tables;
            var columns = new List<Column>
            {
                new(t.TagOrderingId, "INTEGER", true),
                new(t.TagValue, "NVarChar(64)", true),
                new(t.TagSequenceNr, "INTEGER", true),
                new(t.TagPersistenceId, "NVarChar(255)", true)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, t.TagTable, columns, typePadding: 1, constraint: $"[{t.TagOrderingId}], [{t.TagValue}]");
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.TagTable}_{t.TagPersistenceId}_{t.TagSequenceNr}_idx ON {t.TagTable} ({t.TagPersistenceId}, {t.TagSequenceNr})").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.TagTable}_{t.TagValue}_idx ON {t.TagTable} ({t.TagValue});");
            return sb.ToString();
        }

        /// <summary>M1: journal_metadata. No indexes, no trailing semicolon.</summary>
        public static string MetadataDdl(JournalSettings settings)
        {
            var t = settings.Tables;
            var columns = new List<Column>
            {
                new(t.MetadataPersistenceId, "NVarChar(255)", true),
                new(t.MetadataSequenceNumber, "BigInt", true)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, t.Metadata, columns, typePadding: 1,
                constraint: $"[{t.MetadataSequenceNumber}], [{t.MetadataPersistenceId}]");
            return sb.ToString();
        }

        /// <summary>S1: the snapshot table plus its two indexes.</summary>
        public static string SnapshotDdl(SnapshotSettings settings)
        {
            var t = settings.Tables;
            var columns = new List<Column>
            {
                new(t.PersistenceId, "NVarChar(255)", true),
                new(t.SequenceNumber, "BigInt", true),
                new(t.Created, "BigInt", true),
                new(t.Payload, "VarBinary", false),
                new(t.Manifest, "NVarChar(500)", false),
                new(t.SerializerId, "INTEGER", false)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, t.Snapshot, columns, typePadding: 1, constraint: $"[{t.PersistenceId}], [{t.SequenceNumber}]");
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.Snapshot}_{t.SequenceNumber}_idx ON {t.Snapshot} ({t.SequenceNumber})").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {t.Snapshot}_{t.Created}_idx ON {t.Snapshot} ({t.Created});");
            return sb.ToString();
        }

        private static void AppendCreateTable(StringBuilder sb, string table, List<Column> columns, int typePadding, string? constraint)
        {
            var nameWidth = columns.Max(c => c.Name.Length + 2) + 1;
            var typeWidth = columns.Max(c => c.Type.Length) + typePadding;

            sb.Append("CREATE TABLE IF NOT EXISTS [").Append(table).Append("]\n(\n");
            for (var i = 0; i < columns.Count; i++)
            {
                var c = columns[i];
                var isLast = i == columns.Count - 1 && constraint is null;
                sb.Append('\t');
                sb.Append(('[' + c.Name + ']').PadRight(nameWidth));
                sb.Append(c.Type.PadRight(typeWidth));
                sb.Append(c.NotNull ? "NOT NULL" : "    NULL");
                sb.Append(c.Suffix);
                if (!isLast)
                    sb.Append(',');
                sb.Append('\n');
            }

            if (constraint is not null)
            {
                sb.Append('\n');
                sb.Append("\tCONSTRAINT [PK_").Append(table).Append("] PRIMARY KEY (").Append(constraint).Append(")\n");
            }

            sb.Append(')');
        }

        // ---- create + verify -----------------------------------------------------------------------

        /// <summary>
        /// Creates missing journal-side tables when auto-initialize is on, then verifies them.
        /// Returns warnings to log.
        /// </summary>
        public static IReadOnlyList<string> EnsureJournalSchema(SqliteConnection connection, JournalSettings settings, bool requireTagsColumnForReads, bool requireTagTableForReads)
        {
            if (settings.AutoInitialize)
            {
                using var tx = connection.BeginTransaction(deferred: false);
                Execute(connection, tx, JournalDdl(settings));
                if (settings.WritesTagTable)
                    Execute(connection, tx, TagTableDdl(settings));
                if (settings.DeleteCompatibilityMode)
                    Execute(connection, tx, MetadataDdl(settings));
                tx.Commit();
            }

            return VerifyJournalSchema(connection, settings, requireTagsColumnForReads, requireTagTableForReads);
        }

        /// <summary>Creates the snapshot table when auto-initialize is on, then verifies it.</summary>
        public static void EnsureSnapshotSchema(SqliteConnection connection, SnapshotSettings settings)
        {
            if (settings.AutoInitialize)
            {
                using var tx = connection.BeginTransaction(deferred: false);
                Execute(connection, tx, SnapshotDdl(settings));
                tx.Commit();
            }

            VerifySnapshotSchema(connection, settings);
        }

        private static void Execute(SqliteConnection connection, SqliteTransaction tx, string sql)
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        public static IReadOnlyList<string> VerifyJournalSchema(SqliteConnection connection, JournalSettings settings, bool requireTagsColumnForReads = false, bool requireTagTableForReads = false)
        {
            var warnings = new List<string>();
            var t = settings.Tables;
            var dataSource = DataSourceOf(connection);

            var columns = RequireTable(connection, t.Journal, dataSource);
            var needsTagsColumn = settings.WritesTagsColumn || requireTagsColumnForReads;
            const string schema = "the journal schema";
            var required = new List<(string Column, string Reason)>
            {
                (t.Ordering, schema), (t.Created, schema), (t.Deleted, schema), (t.PersistenceId, schema),
                (t.SequenceNumber, schema), (t.Message, schema), (t.Manifest, schema), (t.Identifier, schema)
            };
            if (settings.UseWriterUuid)
                required.Add((t.WriterUuid, "use-writer-uuid-column = true"));
            if (needsTagsColumn)
                required.Add((t.Tags, settings.WritesTagsColumn ? $"tag-write-mode = {settings.TagWriteMode}" : "tag-read-mode = Csv"));
            RequireColumns(columns, t.Journal, required);

            var ordering = columns.First(c => string.Equals(c.Name, t.Ordering, StringComparison.OrdinalIgnoreCase));
            if (ordering.Pk != 1 || !string.Equals(ordering.Type, "INTEGER", StringComparison.OrdinalIgnoreCase))
                throw new SqliteSchemaException($"Column [{t.Ordering}] must be INTEGER PRIMARY KEY (rowid alias).");

            if (!HasUniqueIndex(connection, t.Journal, t.PersistenceId, t.SequenceNumber))
            {
                warnings.Add($"Table [{t.Journal}] has no UNIQUE index on ({t.PersistenceId}, {t.SequenceNumber}); duplicate writes will not be detected.");
            }

            if (settings.WritesTagTable || requireTagTableForReads)
            {
                var tagColumns = RequireTable(connection, t.TagTable, dataSource);
                var tagReason = settings.WritesTagTable ? $"tag-write-mode = {settings.TagWriteMode}" : "tag-read-mode = TagTable";
                RequireColumns(
                    tagColumns, t.TagTable,
                    [(t.TagOrderingId, tagReason), (t.TagValue, tagReason), (t.TagSequenceNr, tagReason), (t.TagPersistenceId, tagReason)]);
            }

            if (settings.DeleteCompatibilityMode)
            {
                var metaColumns = RequireTable(connection, t.Metadata, dataSource);
                const string reason = "delete-compatibility-mode = true";
                RequireColumns(metaColumns, t.Metadata, [(t.MetadataPersistenceId, reason), (t.MetadataSequenceNumber, reason)]);
            }

            return warnings;
        }

        public static void VerifySnapshotSchema(SqliteConnection connection, SnapshotSettings settings)
        {
            var t = settings.Tables;
            var columns = RequireTable(connection, t.Snapshot, DataSourceOf(connection));
            var missing = new[] { t.PersistenceId, t.SequenceNumber, t.Created, t.Payload, t.Manifest, t.SerializerId }
                .Where(r => !columns.Any(c => string.Equals(c.Name, r, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (missing.Length > 0)
            {
                throw new SqliteSchemaException(
                    $"Table [{t.Snapshot}] is missing column(s) [{string.Join(", ", missing)}] required by the snapshot store. This plugin never alters tables.");
            }
        }

        private readonly record struct ColumnInfo(string Name, string Type, long Pk);

        private static string DataSourceOf(SqliteConnection connection)
            => string.IsNullOrEmpty(connection.DataSource) ? "(unnamed)" : connection.DataSource;

        private static List<ColumnInfo> RequireTable(SqliteConnection connection, string table, string dataSource)
        {
            using (var exists = connection.CreateCommand())
            {
                exists.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name = @t COLLATE NOCASE";
                exists.Parameters.AddWithValue("@t", table);
                if (exists.ExecuteScalar() is null)
                {
                    throw new SqliteSchemaException(
                        $"Table [{table}] does not exist in [{dataSource}]. Set auto-initialize = on or create it (see the Akka.Persistence.Embedded docs).");
                }
            }

            var result = new List<ColumnInfo>();
            using var info = connection.CreateCommand();
            info.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = info.ExecuteReader();
            while (reader.Read())
                result.Add(new ColumnInfo(reader.GetString(1), reader.GetString(2), reader.GetInt64(5)));
            return result;
        }

        /// <summary>Throws naming each missing column and the setting (or the schema itself) that requires it.</summary>
        private static void RequireColumns(List<ColumnInfo> columns, string table, IEnumerable<(string Column, string Reason)> required)
        {
            var missing = required
                .Where(r => !columns.Any(c => string.Equals(c.Name, r.Column, StringComparison.OrdinalIgnoreCase)))
                .Select(r => $"{r.Column} (required by {r.Reason})")
                .ToArray();
            if (missing.Length > 0)
                throw new SqliteSchemaException($"Table [{table}] is missing column(s): {string.Join(", ", missing)}. This plugin never alters tables.");
        }

        private static bool HasUniqueIndex(SqliteConnection connection, string table, string first, string second)
        {
            var uniqueIndexes = new List<string>();
            using (var list = connection.CreateCommand())
            {
                list.CommandText = $"PRAGMA index_list(\"{table}\")";
                using var reader = list.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.GetInt64(2) == 1)
                        uniqueIndexes.Add(reader.GetString(1));
                }
            }

            foreach (var index in uniqueIndexes)
            {
                var columns = new List<string>();
                using var info = connection.CreateCommand();
                info.CommandText = $"PRAGMA index_info(\"{index}\")";
                using var reader = info.ExecuteReader();
                while (reader.Read())
                {
                    if (!reader.IsDBNull(2))
                        columns.Add(reader.GetString(2));
                }

                if (columns.Count == 2
                    && columns.Any(c => string.Equals(c, first, StringComparison.OrdinalIgnoreCase))
                    && columns.Any(c => string.Equals(c, second, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
