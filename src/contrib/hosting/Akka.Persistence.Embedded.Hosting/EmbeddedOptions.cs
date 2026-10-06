//-----------------------------------------------------------------------
// <copyright file="EmbeddedOptions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Globalization;
using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Embedded.Journal;
using Akka.Persistence.Embedded.Snapshot;

namespace Akka.Persistence.Embedded.Hosting
{
    internal static class HoconText
    {
        public static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        public static string Bool(bool value) => value ? "true" : "false";

        public static string Milliseconds(TimeSpan value) => value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";
    }

    /// <summary>
    /// Options of the SQLite journal and of the read journal that reads it, shaped like Akka.Persistence.Sql.Hosting's
    /// <c>SqlJournalOptions</c>. A property left null keeps the plugin's reference setting. The plugin is registered in code
    /// by type (<c>JournalOptions&lt;SqliteWriteJournal, SqliteReadJournalProvider&gt;</c>), so it needs no <c>class</c> setting
    /// and starts with <c>Akka.DynamicTypeLoading</c> off.
    /// </summary>
    public sealed class EmbeddedJournalOptions : Akka.Persistence.Hosting.JournalOptions<SqliteWriteJournal, Akka.Persistence.Embedded.Query.SqliteReadJournalProvider>
    {
        private static readonly Config Default = SqlitePersistence.DefaultJournalConfiguration;

        /// <summary>Creates options for the default journal, <c>akka.persistence.journal.embedded</c>.</summary>
        public EmbeddedJournalOptions() : this(true)
        {
        }

        /// <summary>Creates options for a journal registered under <paramref name="identifier"/>.</summary>
        public EmbeddedJournalOptions(bool isDefaultPlugin, string identifier = "embedded") : base(isDefaultPlugin)
        {
            Identifier = identifier;
            Serializer = null;
            AutoInitialize = true;
        }

        /// <inheritdoc />
        public override string Identifier { get; set; }

        /// <summary>
        /// Microsoft.Data.Sqlite connection string. Required. <c>Default Timeout</c> sets how long a busy database is waited on.
        /// The plugin adds <c>Pooling=False</c> to its long-lived connections unless the string sets <c>Pooling</c>.
        /// </summary>
        public string? ConnectionString { get; set; }

        /// <summary>Name of the journal table.</summary>
        public string? JournalTableName { get; set; }

        /// <summary>Name of the tag table.</summary>
        public string? TagTableName { get; set; }

        /// <summary>Write requests queued for the writer thread. Further writes fail.</summary>
        public int? BufferSize { get; set; }

        /// <summary>Rows per write transaction.</summary>
        public int? BatchSize { get; set; }

        /// <summary>Rows per round trip during recovery.</summary>
        public int? ReplayBatchSize { get; set; }

        /// <summary>Threads that run recovery reads.</summary>
        public int? ReadThreads { get; set; }

        /// <summary>Poll interval of the read journal's live queries.</summary>
        public TimeSpan? QueryRefreshInterval { get; set; }

        /// <summary>Rows per read journal round trip.</summary>
        public int? QueryMaxBufferSize { get; set; }

        /// <summary>Threads that run read journal queries.</summary>
        public int? QueryThreads { get; set; }

        /// <inheritdoc />
        protected override Config InternalDefaultConfig => Default;

        /// <summary>The read journal's plugin id: <c>akka.persistence.query.journal.{Identifier}</c>.</summary>
        public string QueryPluginId => ReadJournalPluginId;

        /// <inheritdoc />
        protected override StringBuilder Build(StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");

            sb.AppendLine($"connection-string = {HoconText.Quote(ConnectionString!)}");
            if (JournalTableName is not null)
                sb.AppendLine($"table-name = {HoconText.Quote(JournalTableName)}");
            if (TagTableName is not null)
                sb.AppendLine($"tag-table-name = {HoconText.Quote(TagTableName)}");
            if (BufferSize is not null)
                sb.AppendLine($"buffer-size = {BufferSize}");
            if (BatchSize is not null)
                sb.AppendLine($"batch-size = {BatchSize}");
            if (ReplayBatchSize is not null)
                sb.AppendLine($"replay-batch-size = {ReplayBatchSize}");
            if (ReadThreads is not null)
                sb.AppendLine($"read-threads = {ReadThreads}");

            base.Build(sb);

            BuildQueryConfig(sb, QueryPluginId);

            return sb;
        }

        private void BuildQueryConfig(StringBuilder sb, string queryPluginId)
        {
            sb.AppendLine($"{queryPluginId} {{");
            sb.AppendLine($"write-plugin = {HoconText.Quote(PluginId)}");
            if (QueryRefreshInterval is not null)
                sb.AppendLine($"refresh-interval = {HoconText.Milliseconds(QueryRefreshInterval.Value)}");
            if (QueryMaxBufferSize is not null)
                sb.AppendLine($"max-buffer-size = {QueryMaxBufferSize}");
            if (QueryThreads is not null)
                sb.AppendLine($"query-threads = {QueryThreads}");
            sb.AppendLine("}");
        }
    }

    /// <summary>Options of the SQLite snapshot store, shaped like Akka.Persistence.Sql.Hosting's <c>SqlSnapshotOptions</c>.</summary>
    public sealed class EmbeddedSnapshotOptions : Akka.Persistence.Hosting.SnapshotOptions<SqliteSnapshotStore>
    {
        private static readonly Config Default = SqlitePersistence.DefaultSnapshotConfiguration;

        /// <summary>Creates options for the default snapshot store, <c>akka.persistence.snapshot-store.embedded</c>.</summary>
        public EmbeddedSnapshotOptions() : this(true)
        {
        }

        /// <summary>Creates options for a snapshot store registered under <paramref name="identifier"/>.</summary>
        public EmbeddedSnapshotOptions(bool isDefaultPlugin, string identifier = "embedded") : base(isDefaultPlugin)
        {
            Identifier = identifier;
            Serializer = null;
            AutoInitialize = true;
        }

        /// <inheritdoc />
        public override string Identifier { get; set; }

        /// <summary>Microsoft.Data.Sqlite connection string. Required.</summary>
        public string? ConnectionString { get; set; }

        /// <summary>Name of the snapshot table.</summary>
        public string? TableName { get; set; }

        /// <inheritdoc />
        protected override Config InternalDefaultConfig => Default;

        /// <inheritdoc />
        protected override StringBuilder Build(StringBuilder sb)
        {
            if (string.IsNullOrWhiteSpace(ConnectionString))
                throw new ArgumentNullException(nameof(ConnectionString), $"{nameof(ConnectionString)} can not be null or empty.");

            sb.AppendLine($"connection-string = {HoconText.Quote(ConnectionString!)}");
            if (TableName is not null)
                sb.AppendLine($"table-name = {HoconText.Quote(TableName)}");

            return base.Build(sb);
        }
    }
}
