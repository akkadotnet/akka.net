//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Configuration;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>Names of the journal, metadata and tag tables and their columns.</summary>
    internal sealed record JournalTableNames(
        string Journal, string Ordering, string Deleted, string PersistenceId, string SequenceNumber,
        string Created, string Tags, string Message, string Identifier, string Manifest, string WriterUuid,
        string Metadata, string MetadataPersistenceId, string MetadataSequenceNumber,
        string TagTable, string TagOrderingId, string TagValue, string TagPersistenceId, string TagSequenceNr);

    /// <summary>Names of the snapshot table and its columns.</summary>
    internal sealed record SnapshotTableNames(
        string Snapshot, string PersistenceId, string SequenceNumber, string Created,
        string Payload, string Manifest, string SerializerId);

    /// <summary>How the read journal finds the tags of an event.</summary>
    internal enum TagReadMode
    {
        TagTable,
        Csv
    }

    /// <summary>Shared parsing and validation helpers for the three settings records.</summary>
    internal static class SettingsHelper
    {
        private static readonly string[] SqlOnlyKeys =
        [
            "provider-name", "parallelism", "db-round-trip-max-batch-size", "db-round-trip-max-tag-batch-size",
            "prefer-parameters-on-multirow-insert", "max-row-by-row-size", "use-clone-connection",
            "materializer-dispatcher", "dao", "read-isolation-level", "write-isolation-level", "use-shared-db",
            "compatibility-mode", "add-shutdown-hook", "sql-server", "sqlserver", "sqlite", "postgresql", "mysql"
        ];

        private static readonly string[] SqliteLegacyKeys =
        [
            "table-name", "metadata-table-name", "timestamp-provider", "connection-timeout", "schema-name"
        ];

        private static readonly string[] QueryIgnoredKeys =
        [
            "tag-separator", "table-mapping", "default", "delete-compatibility-mode", "buffer-size", "batch-size",
            "replay-batch-size", "parallelism", "provider-name", "dao", "table-compatibility-mode", "schema-name"
        ];

        public static Config UserSection(Config pluginConfig, string pluginPath, Config systemConfig)
        {
            var section = systemConfig.GetConfig(pluginPath);
            return section is null || section.IsEmpty ? pluginConfig : section;
        }

        public static string RequireConnectionString(Config config, string path, string key = "connection-string")
        {
            var value = config.GetString(key, "");
            if (string.IsNullOrWhiteSpace(value))
                throw new ConfigurationException($"[{path}.{key}] is required.");
            ValidateConnectionString(value, path, key);
            return value;
        }

        public static void ValidateConnectionString(string connectionString, string path, string key = "connection-string")
        {
            SqliteConnectionStringBuilder builder;
            try
            {
                builder = new SqliteConnectionStringBuilder(connectionString);
            }
            catch (Exception e) when (e is ArgumentException or FormatException)
            {
                throw new ConfigurationException($"[{path}.{key}] is not a valid Microsoft.Data.Sqlite connection string: {e.Message}", e);
            }

            var isPrivateMemory = string.Equals(builder.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase)
                || (builder.Mode == SqliteOpenMode.Memory && builder.Cache != SqliteCacheMode.Shared);
            if (isPrivateMemory)
            {
                throw new ConfigurationException(
                    $"[{path}.{key}] uses a private in-memory database; every connection would see a different database. " +
                    "Use \"Data Source=<name>;Mode=Memory;Cache=Shared\" or a file.");
            }
        }

        public static void ValidateIdentifier(string path, string value)
        {
            var ok = value.Length > 0 && (char.IsAsciiLetter(value[0]) || value[0] == '_');
            for (var i = 1; ok && i < value.Length; i++)
                ok = char.IsAsciiLetterOrDigit(value[i]) || value[i] == '_';
            if (!ok)
                throw new ConfigurationException($"[{path}] = [{value}] is not a plain SQL identifier.");
        }

        public static string Identifier(Config config, string pluginPath, string key)
        {
            var value = config.GetString(key, "");
            ValidateIdentifier($"{pluginPath}.{key}", value);
            return value;
        }

        public static int PositiveInt(Config config, string pluginPath, string key, int min = 1, int max = int.MaxValue)
        {
            var value = config.GetInt(key);
            if (value < min || value > max)
                throw new ConfigurationException($"[{pluginPath}.{key}] must be between {min} and {max}. Found [{value}].");
            return value;
        }

        /// <summary>Throws for APS settings that this plugin refuses and returns warnings for settings it ignores.</summary>
        public static ImmutableArray<string> CheckSqlSettings(Config user, string pluginPath, bool isJournalOrSnapshot = true)
        {
            var tableMapping = user.GetString("table-mapping", "default");
            if (!string.IsNullOrEmpty(tableMapping) && !string.Equals(tableMapping, "default", StringComparison.Ordinal))
                throw UnsupportedTableMapping(pluginPath, tableMapping);

            if (user.HasPath("table-compatibility-mode"))
                throw UnsupportedTableMapping(pluginPath, user.GetString("table-compatibility-mode", ""));

            var schemaName = user.GetString("default.schema-name", null);
            if (!string.IsNullOrEmpty(schemaName))
            {
                throw new ConfigurationException(
                    $"[{pluginPath}.default.schema-name] must be null: SQLite has no schemas. Found [{schemaName}].");
            }

            foreach (var key in SqliteLegacyKeys)
            {
                if (!user.HasPath(key))
                    continue;

                throw new ConfigurationException(
                    $"[{pluginPath}.{key}] is an Akka.Persistence.Sqlite (Sql.Common) setting. " +
                    $"This plugin uses the Akka.Persistence.Sql schema; set table names under {pluginPath}.default.* instead. See the migration guide.");
            }

            var ignored = SqlOnlyKeys.Where(user.HasPath).ToArray();
            return ignored.Length == 0
                ? ImmutableArray<string>.Empty
                : [$"[{pluginPath}] ignores Akka.Persistence.Sql setting(s) [{string.Join(", ", ignored)}]: they have no meaning for Akka.Persistence.Embedded."];
        }

        public static ImmutableArray<string> CheckQuerySettings(Config user, string pluginPath, string writePlugin)
        {
            var ignored = QueryIgnoredKeys.Concat(SqlOnlyKeys).Distinct().Where(user.HasPath).ToArray();
            return ignored.Length == 0
                ? ImmutableArray<string>.Empty
                : [$"[{pluginPath}] ignores setting(s) [{string.Join(", ", ignored)}]: table names, tag settings and modes come from the write plugin [{writePlugin}]."];
        }

        private static ConfigurationException UnsupportedTableMapping(string pluginPath, string value)
            => new($"[{pluginPath}] only supports table-mapping = default (the Akka.Persistence.Sql default schema). " +
                   $"Found [{value}]. Legacy table mappings are not supported by Akka.Persistence.Embedded.");
    }

    /// <summary>Validated settings of the journal.</summary>
    internal sealed record JournalSettings
    {
        public required string PluginPath { get; init; }
        public required string ConnectionString { get; init; }
        public required bool DeleteCompatibilityMode { get; init; }
        public required TagWriteMode TagWriteMode { get; init; }
        public required string TagSeparator { get; init; }
        public required bool AutoInitialize { get; init; }
        public required string? DefaultSerializer { get; init; }
        public required int BufferSize { get; init; }
        public required int BatchSize { get; init; }
        public required int ReplayBatchSize { get; init; }
        public required int ReadThreads { get; init; }
        public required bool UseWriterUuid { get; init; }
        public required JournalTableNames Tables { get; init; }
        public required ImmutableArray<string> Warnings { get; init; }

        /// <summary>True when events carry a tags column (Csv and Both).</summary>
        public bool WritesTagsColumn => TagWriteMode != TagWriteMode.TagTable;

        /// <summary>True when events go to the tag table (TagTable and Both).</summary>
        public bool WritesTagTable => TagWriteMode != TagWriteMode.Csv;

        public static JournalSettings Create(Config pluginConfig, string pluginPath, Config systemConfig)
        {
            var user = SettingsHelper.UserSection(pluginConfig, pluginPath, systemConfig);
            var warnings = SettingsHelper.CheckSqlSettings(user, pluginPath);
            var config = pluginConfig.WithFallback(SqlitePersistence.DefaultJournalConfiguration);

            var modeText = config.GetString("tag-write-mode", "TagTable");
            if (!Enum.TryParse<TagWriteMode>(modeText, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
            {
                throw new ConfigurationException(
                    $"[{pluginPath}.tag-write-mode] must be Csv, TagTable or Both. Found [{modeText}].");
            }

            var separator = config.GetString("tag-separator", ";");
            if (string.IsNullOrEmpty(separator))
                throw new ConfigurationException($"[{pluginPath}.tag-separator] must not be empty.");

            var serializer = config.GetString("serializer", null);
            if (string.IsNullOrEmpty(serializer))
                serializer = null;

            const string j = "default.journal.columns.";
            const string m = "default.metadata.columns.";
            const string t = "default.tag.columns.";
            var tables = new JournalTableNames(
                Journal: SettingsHelper.Identifier(config, pluginPath, "default.journal.table-name"),
                Ordering: SettingsHelper.Identifier(config, pluginPath, j + "ordering"),
                Deleted: SettingsHelper.Identifier(config, pluginPath, j + "deleted"),
                PersistenceId: SettingsHelper.Identifier(config, pluginPath, j + "persistence-id"),
                SequenceNumber: SettingsHelper.Identifier(config, pluginPath, j + "sequence-number"),
                Created: SettingsHelper.Identifier(config, pluginPath, j + "created"),
                Tags: SettingsHelper.Identifier(config, pluginPath, j + "tags"),
                Message: SettingsHelper.Identifier(config, pluginPath, j + "message"),
                Identifier: SettingsHelper.Identifier(config, pluginPath, j + "identifier"),
                Manifest: SettingsHelper.Identifier(config, pluginPath, j + "manifest"),
                WriterUuid: SettingsHelper.Identifier(config, pluginPath, j + "writer-uuid"),
                Metadata: SettingsHelper.Identifier(config, pluginPath, "default.metadata.table-name"),
                MetadataPersistenceId: SettingsHelper.Identifier(config, pluginPath, m + "persistence-id"),
                MetadataSequenceNumber: SettingsHelper.Identifier(config, pluginPath, m + "sequence-number"),
                TagTable: SettingsHelper.Identifier(config, pluginPath, "default.tag.table-name"),
                TagOrderingId: SettingsHelper.Identifier(config, pluginPath, t + "ordering-id"),
                TagValue: SettingsHelper.Identifier(config, pluginPath, t + "tag-value"),
                TagPersistenceId: SettingsHelper.Identifier(config, pluginPath, t + "persistence-id"),
                TagSequenceNr: SettingsHelper.Identifier(config, pluginPath, t + "sequence-nr"));

            return new JournalSettings
            {
                PluginPath = pluginPath,
                ConnectionString = SettingsHelper.RequireConnectionString(config, pluginPath),
                DeleteCompatibilityMode = config.GetBoolean("delete-compatibility-mode"),
                TagWriteMode = mode,
                TagSeparator = separator,
                AutoInitialize = config.GetBoolean("auto-initialize", true),
                DefaultSerializer = serializer,
                BufferSize = SettingsHelper.PositiveInt(config, pluginPath, "buffer-size"),
                BatchSize = SettingsHelper.PositiveInt(config, pluginPath, "batch-size"),
                ReplayBatchSize = SettingsHelper.PositiveInt(config, pluginPath, "replay-batch-size"),
                ReadThreads = SettingsHelper.PositiveInt(config, pluginPath, "read-threads", 1, 64),
                UseWriterUuid = config.GetBoolean("default.journal.use-writer-uuid-column", true),
                Tables = tables,
                Warnings = warnings
            };
        }
    }

    /// <summary>Validated settings of the snapshot store.</summary>
    internal sealed record SnapshotSettings
    {
        public required string PluginPath { get; init; }
        public required string ConnectionString { get; init; }
        public required bool AutoInitialize { get; init; }
        public required string? DefaultSerializer { get; init; }
        public required SnapshotTableNames Tables { get; init; }
        public required ImmutableArray<string> Warnings { get; init; }

        public static SnapshotSettings Create(Config pluginConfig, string pluginPath, Config systemConfig)
        {
            var user = SettingsHelper.UserSection(pluginConfig, pluginPath, systemConfig);
            var warnings = SettingsHelper.CheckSqlSettings(user, pluginPath);
            var config = pluginConfig.WithFallback(SqlitePersistence.DefaultSnapshotConfiguration);

            var serializer = config.GetString("serializer", null);
            if (string.IsNullOrEmpty(serializer))
                serializer = null;

            const string c = "default.snapshot.columns.";
            var tables = new SnapshotTableNames(
                Snapshot: SettingsHelper.Identifier(config, pluginPath, "default.snapshot.table-name"),
                PersistenceId: SettingsHelper.Identifier(config, pluginPath, c + "persistence-id"),
                SequenceNumber: SettingsHelper.Identifier(config, pluginPath, c + "sequence-number"),
                Created: SettingsHelper.Identifier(config, pluginPath, c + "created"),
                Payload: SettingsHelper.Identifier(config, pluginPath, c + "snapshot"),
                Manifest: SettingsHelper.Identifier(config, pluginPath, c + "manifest"),
                SerializerId: SettingsHelper.Identifier(config, pluginPath, c + "serializerId"));

            return new SnapshotSettings
            {
                PluginPath = pluginPath,
                ConnectionString = SettingsHelper.RequireConnectionString(config, pluginPath),
                AutoInitialize = config.GetBoolean("auto-initialize", true),
                DefaultSerializer = serializer,
                Tables = tables,
                Warnings = warnings
            };
        }
    }

    /// <summary>Settings of the optional gap-tracking actor.</summary>
    internal sealed record JournalSequenceSettings(
        bool Enabled, int BatchSize, int MaxTries, TimeSpan QueryDelay, TimeSpan MaxBackoffQueryDelay, TimeSpan AskTimeout);

    /// <summary>Validated settings of the read journal.</summary>
    internal sealed record QuerySettings
    {
        public required string PluginPath { get; init; }

        /// <summary>The write plugin id as core knows it (may be empty = default journal).</summary>
        public required string WritePluginId { get; init; }

        /// <summary>Config path of the write plugin section.</summary>
        public required string WritePluginPath { get; init; }

        public required JournalSettings Journal { get; init; }
        public required string ConnectionString { get; init; }
        public required TagReadMode TagReadMode { get; init; }
        public required int MaxBufferSize { get; init; }
        public required TimeSpan RefreshInterval { get; init; }
        public required int MaxConcurrentQueries { get; init; }
        public required TimeSpan ThrottleTimeout { get; init; }
        public required int QueryThreads { get; init; }
        public required TimeSpan WritePluginInitTimeout { get; init; }
        public required JournalSequenceSettings SequenceRetrieval { get; init; }
        public required ImmutableArray<string> Warnings { get; init; }

        public static QuerySettings Create(Config queryConfig, string pluginPath, Config systemConfig)
        {
            var user = SettingsHelper.UserSection(queryConfig, pluginPath, systemConfig);
            var config = queryConfig.WithFallback(SqlitePersistence.DefaultQueryConfiguration);

            var writePluginId = config.GetString("write-plugin", "") ?? "";
            var writePluginPath = string.IsNullOrEmpty(writePluginId)
                ? systemConfig.GetString("akka.persistence.journal.plugin", "") ?? ""
                : writePluginId;
            if (string.IsNullOrEmpty(writePluginPath) || !systemConfig.HasPath(writePluginPath))
            {
                throw new ConfigurationException(
                    $"[{pluginPath}.write-plugin] names [{writePluginPath}], which is not a configured journal plugin section.");
            }

            var writeSection = systemConfig.GetConfig(writePluginPath);
            var writeClass = writeSection.GetString("class", "") ?? "";
            if (!IsEmbeddedJournalClass(writeClass))
            {
                throw new ConfigurationException(
                    $"[{pluginPath}.write-plugin] names [{writePluginPath}], whose class [{writeClass}] is not the Akka.Persistence.Embedded journal.");
            }

            var journal = JournalSettings.Create(
                writeSection.WithFallback(SqlitePersistence.DefaultJournalConfiguration), writePluginPath, systemConfig);

            var connectionString = config.GetString("connection-string", "");
            if (string.IsNullOrWhiteSpace(connectionString))
                connectionString = journal.ConnectionString;
            else
                SettingsHelper.ValidateConnectionString(connectionString, pluginPath);

            var readModeText = config.GetString("tag-read-mode", "auto");
            TagReadMode readMode;
            if (string.Equals(readModeText, "auto", StringComparison.OrdinalIgnoreCase))
                readMode = journal.TagWriteMode == TagWriteMode.Csv ? TagReadMode.Csv : TagReadMode.TagTable;
            else if (string.Equals(readModeText, "Csv", StringComparison.OrdinalIgnoreCase))
                readMode = TagReadMode.Csv;
            else if (string.Equals(readModeText, "TagTable", StringComparison.OrdinalIgnoreCase))
                readMode = TagReadMode.TagTable;
            else
                throw new ConfigurationException($"[{pluginPath}.tag-read-mode] must be auto, Csv or TagTable. Found [{readModeText}].");

            var seq = "journal-sequence-retrieval.";
            var sequence = new JournalSequenceSettings(
                Enabled: config.GetBoolean(seq + "enabled"),
                BatchSize: SettingsHelper.PositiveInt(config, pluginPath, seq + "batch-size"),
                MaxTries: SettingsHelper.PositiveInt(config, pluginPath, seq + "max-tries"),
                QueryDelay: config.GetTimeSpan(seq + "query-delay", TimeSpan.FromSeconds(1)),
                MaxBackoffQueryDelay: config.GetTimeSpan(seq + "max-backoff-query-delay", TimeSpan.FromSeconds(60)),
                AskTimeout: config.GetTimeSpan(seq + "ask-timeout", TimeSpan.FromSeconds(1)));

            return new QuerySettings
            {
                PluginPath = pluginPath,
                WritePluginId = writePluginId,
                WritePluginPath = writePluginPath,
                Journal = journal,
                ConnectionString = connectionString,
                TagReadMode = readMode,
                MaxBufferSize = SettingsHelper.PositiveInt(config, pluginPath, "max-buffer-size"),
                RefreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(1)),
                MaxConcurrentQueries = SettingsHelper.PositiveInt(config, pluginPath, "max-concurrent-queries"),
                ThrottleTimeout = config.GetTimeSpan("query-throttle-timeout", TimeSpan.FromSeconds(3)),
                QueryThreads = SettingsHelper.PositiveInt(config, pluginPath, "query-threads", 1, 64),
                WritePluginInitTimeout = config.GetTimeSpan("write-plugin-init-timeout", TimeSpan.FromSeconds(10)),
                SequenceRetrieval = sequence,
                Warnings = SettingsHelper.CheckQuerySettings(user, pluginPath, writePluginPath)
            };
        }

        private static bool IsEmbeddedJournalClass(string classValue)
        {
            var comma = classValue.IndexOf(',');
            var name = (comma < 0 ? classValue : classValue[..comma]).Trim();
            if (!string.Equals(name, "Akka.Persistence.Embedded.Journal.SqliteWriteJournal", StringComparison.Ordinal))
                return false;

            if (comma < 0)
                return true;

            var assembly = classValue[(comma + 1)..].Split(',')[0].Trim();
            return string.Equals(assembly, "Akka.Persistence.Embedded", StringComparison.OrdinalIgnoreCase);
        }
    }
}
