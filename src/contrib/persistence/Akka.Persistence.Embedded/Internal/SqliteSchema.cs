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
    /// Builds the exact DDL text Akka.Persistence.Sql (linq2db) sends to SQLite for its default layout (tag table, no
    /// journal_metadata), creates missing tables and verifies that the tables have what the plugin needs. Never alters or drops anything.
    /// </summary>
    internal static class SqliteSchema
    {
        private const string StatementEnd = ";\r\n";

        private readonly record struct Column(string Name, string Type, bool NotNull, string Suffix = "");

        // ---- DDL text ------------------------------------------------------------------------------

        /// <summary>The journal table plus its three indexes.</summary>
        public static string JournalDdl(JournalSettings settings)
        {
            var table = settings.Tables.Journal;
            var columns = new List<Column>
            {
                new("ordering", "INTEGER", true, " PRIMARY KEY AUTOINCREMENT"),
                new("created", "BigInt", true),
                new("deleted", "Bit", true),
                new("persistence_id", "NVarChar(255)", true),
                new("sequence_number", "BigInt", true),
                new("message", "VarBinary", true),
                new("manifest", "NVarChar(500)", false),
                new("identifier", "INTEGER", false),
                new("writer_uuid", "NVarChar(128)", false)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, table, columns, typePadding: 2, constraint: null);
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE UNIQUE INDEX IF NOT EXISTS {table}_uq ON {table} (persistence_id, sequence_number)").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_created_idx ON {table} (created)").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_sequence_number_idx ON {table} (sequence_number);");
            return sb.ToString();
        }

        /// <summary>The tag table plus its two indexes.</summary>
        public static string TagTableDdl(JournalSettings settings)
        {
            var table = settings.Tables.TagTable;
            var columns = new List<Column>
            {
                new("ordering_id", "INTEGER", true),
                new("tag", "NVarChar(64)", true),
                new("sequence_nr", "INTEGER", true),
                new("persistence_id", "NVarChar(255)", true)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, table, columns, typePadding: 1, constraint: "[ordering_id], [tag]");
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_persistence_id_sequence_nr_idx ON {table} (persistence_id, sequence_nr)").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_tag_idx ON {table} (tag);");
            return sb.ToString();
        }

        /// <summary>The snapshot table plus its two indexes.</summary>
        public static string SnapshotDdl(SnapshotSettings settings)
        {
            var table = settings.TableName;
            var columns = new List<Column>
            {
                new("persistence_id", "NVarChar(255)", true),
                new("sequence_number", "BigInt", true),
                new("created", "BigInt", true),
                new("snapshot", "VarBinary", false),
                new("manifest", "NVarChar(500)", false),
                new("serializer_id", "INTEGER", false)
            };

            var sb = new StringBuilder();
            AppendCreateTable(sb, table, columns, typePadding: 1, constraint: "[persistence_id], [sequence_number]");
            sb.Append("\n;").Append("\r\n");
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_sequence_number_idx ON {table} (sequence_number)").Append(StatementEnd);
            sb.Append($"CREATE INDEX IF NOT EXISTS {table}_created_idx ON {table} (created);");
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
        /// Creates the journal and tag tables when auto-initialize is on, then verifies them.
        /// Returns warnings to log.
        /// </summary>
        public static IReadOnlyList<string> EnsureJournalSchema(SqliteConnection connection, JournalSettings settings)
        {
            if (settings.AutoInitialize)
            {
                using var tx = connection.BeginTransaction(deferred: false);
                Execute(connection, tx, JournalDdl(settings));
                Execute(connection, tx, TagTableDdl(settings));
                tx.Commit();
            }

            return VerifyJournalSchema(connection, settings);
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

        /// <summary>
        /// Checks that the journal and tag tables have every column the plugin uses and that the ordering column is a rowid alias.
        /// Extra columns are fine (the Akka.Persistence.Sql docs DDL has a <c>tags</c> column that is never written here).
        /// A missing unique index only produces a warning.
        /// </summary>
        public static IReadOnlyList<string> VerifyJournalSchema(SqliteConnection connection, JournalSettings settings)
        {
            var warnings = new List<string>();
            var t = settings.Tables;
            var dataSource = DataSourceOf(connection);

            var columns = RequireTable(connection, t.Journal, dataSource);
            RequireColumns(
                columns, t.Journal,
                ["ordering", "created", "deleted", "persistence_id", "sequence_number", "message", "manifest", "identifier", "writer_uuid"]);

            var ordering = columns.First(c => string.Equals(c.Name, "ordering", StringComparison.OrdinalIgnoreCase));
            if (ordering.Pk != 1 || !string.Equals(ordering.Type, "INTEGER", StringComparison.OrdinalIgnoreCase))
                throw new SqliteSchemaException("Column [ordering] must be INTEGER PRIMARY KEY (rowid alias).");

            if (!HasUniqueIndex(connection, t.Journal, "persistence_id", "sequence_number"))
            {
                warnings.Add($"Table [{t.Journal}] has no UNIQUE index on (persistence_id, sequence_number); duplicate writes will not be detected.");
            }

            var tagColumns = RequireTable(connection, t.TagTable, dataSource);
            RequireColumns(tagColumns, t.TagTable, ["ordering_id", "tag", "sequence_nr", "persistence_id"]);

            return warnings;
        }

        public static void VerifySnapshotSchema(SqliteConnection connection, SnapshotSettings settings)
        {
            var columns = RequireTable(connection, settings.TableName, DataSourceOf(connection));
            RequireColumns(columns, settings.TableName, ["persistence_id", "sequence_number", "created", "snapshot", "manifest", "serializer_id"]);
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

        /// <summary>Throws naming each missing column.</summary>
        private static void RequireColumns(List<ColumnInfo> columns, string table, IEnumerable<string> required)
        {
            var missing = required
                .Where(r => !columns.Any(c => string.Equals(c.Name, r, StringComparison.OrdinalIgnoreCase)))
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
