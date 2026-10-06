//-----------------------------------------------------------------------
// <copyright file="Settings.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Configuration;
using Microsoft.Data.Sqlite;

namespace Akka.Persistence.Embedded.Internal
{
    /// <summary>Names of the journal table and the tag table. The column names are fixed.</summary>
    internal sealed record JournalTableNames(string Journal, string TagTable);

    /// <summary>Shared parsing and validation helpers for the three settings records.</summary>
    internal static class SettingsHelper
    {
        public static string RequireConnectionString(Config config, string path)
        {
            const string key = "connection-string";
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
    }

    /// <summary>Validated settings of the journal.</summary>
    internal sealed record JournalSettings
    {
        public required string PluginPath { get; init; }
        public required string ConnectionString { get; init; }
        public required bool AutoInitialize { get; init; }
        public required int BufferSize { get; init; }
        public required int BatchSize { get; init; }
        public required int ReplayBatchSize { get; init; }
        public required int ReadThreads { get; init; }
        public required JournalTableNames Tables { get; init; }

        public static JournalSettings Create(Config pluginConfig, string pluginPath)
        {
            var config = pluginConfig.WithFallback(SqlitePersistence.DefaultJournalConfiguration);

            return new JournalSettings
            {
                PluginPath = pluginPath,
                ConnectionString = SettingsHelper.RequireConnectionString(config, pluginPath),
                AutoInitialize = config.GetBoolean("auto-initialize", true),
                BufferSize = SettingsHelper.PositiveInt(config, pluginPath, "buffer-size"),
                BatchSize = SettingsHelper.PositiveInt(config, pluginPath, "batch-size"),
                ReplayBatchSize = SettingsHelper.PositiveInt(config, pluginPath, "replay-batch-size"),
                ReadThreads = SettingsHelper.PositiveInt(config, pluginPath, "read-threads", 1, 64),
                Tables = new JournalTableNames(
                    Journal: SettingsHelper.Identifier(config, pluginPath, "table-name"),
                    TagTable: SettingsHelper.Identifier(config, pluginPath, "tag-table-name"))
            };
        }
    }

    /// <summary>Validated settings of the snapshot store.</summary>
    internal sealed record SnapshotSettings
    {
        public required string PluginPath { get; init; }
        public required string ConnectionString { get; init; }
        public required bool AutoInitialize { get; init; }
        public required string TableName { get; init; }

        public static SnapshotSettings Create(Config pluginConfig, string pluginPath)
        {
            var config = pluginConfig.WithFallback(SqlitePersistence.DefaultSnapshotConfiguration);

            return new SnapshotSettings
            {
                PluginPath = pluginPath,
                ConnectionString = SettingsHelper.RequireConnectionString(config, pluginPath),
                AutoInitialize = config.GetBoolean("auto-initialize", true),
                TableName = SettingsHelper.Identifier(config, pluginPath, "table-name")
            };
        }
    }

    /// <summary>Validated settings of the read journal.</summary>
    internal sealed record QuerySettings
    {
        /// <summary>At most this many queries run or wait at once.</summary>
        public const int MaxConcurrentQueries = 100;

        /// <summary>A query that cannot start within this time fails with a <see cref="TimeoutException"/>.</summary>
        public static readonly TimeSpan ThrottleTimeout = TimeSpan.FromSeconds(3);

        /// <summary>How long a query waits for the write plugin to finish initializing (table creation).</summary>
        public static readonly TimeSpan WritePluginInitTimeout = TimeSpan.FromSeconds(10);

        public required string PluginPath { get; init; }

        /// <summary>The write plugin id as core knows it (may be empty = default journal).</summary>
        public required string WritePluginId { get; init; }

        /// <summary>Config path of the write plugin section.</summary>
        public required string WritePluginPath { get; init; }

        /// <summary>The write plugin's settings. The read journal uses its connection string and table names.</summary>
        public required JournalSettings Journal { get; init; }

        public required int MaxBufferSize { get; init; }
        public required TimeSpan RefreshInterval { get; init; }
        public required int QueryThreads { get; init; }

        public static QuerySettings Create(Config queryConfig, string pluginPath, Config systemConfig)
        {
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
            // a journal registered in a PersistenceSetup has no `class` in HOCON; only a class that names another journal is wrong
            var writeClass = writeSection.GetString("class", "") ?? "";
            if (writeClass.Length > 0 && !IsEmbeddedJournalClass(writeClass))
            {
                throw new ConfigurationException(
                    $"[{pluginPath}.write-plugin] names [{writePluginPath}], whose class [{writeClass}] is not the Akka.Persistence.Embedded journal.");
            }

            return new QuerySettings
            {
                PluginPath = pluginPath,
                WritePluginId = writePluginId,
                WritePluginPath = writePluginPath,
                Journal = JournalSettings.Create(writeSection, writePluginPath),
                MaxBufferSize = SettingsHelper.PositiveInt(config, pluginPath, "max-buffer-size"),
                RefreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(1)),
                QueryThreads = SettingsHelper.PositiveInt(config, pluginPath, "query-threads", 1, 64)
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
