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

        public static string Seconds(TimeSpan value) => value.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + "ms";
    }

    /// <summary>
    /// Options of the SQLite journal. The default config is the plugin's reference configuration, and the plugin
    /// is registered in code, so it needs no <c>class</c> setting and starts with <c>Akka.DynamicTypeLoading</c> off.
    /// </summary>
    public sealed class EmbeddedJournalOptions : Akka.Persistence.Hosting.JournalOptions
    {
        /// <summary>Creates options for the default journal, <c>akka.persistence.journal.embedded</c>.</summary>
        public EmbeddedJournalOptions() : this(true)
        {
        }

        /// <summary>Creates options for a journal registered under <paramref name="identifier"/>.</summary>
        public EmbeddedJournalOptions(bool isDefaultPlugin, string identifier = "embedded") : base(isDefaultPlugin)
        {
            Identifier = identifier;
            AutoInitialize = true;
        }

        /// <inheritdoc />
        public override string Identifier { get; set; }

        /// <summary>Microsoft.Data.Sqlite connection string. Required. <c>Default Timeout</c> sets how long a busy database is waited on.</summary>
        public string ConnectionString { get; set; } = "";

        /// <summary>Where tags are stored. Default <see cref="TagWriteMode.TagTable"/>.</summary>
        public TagWriteMode TagWriteMode { get; set; } = TagWriteMode.TagTable;

        /// <summary>Separator of the Csv tags column. Used with <see cref="TagWriteMode.Csv"/> and <see cref="TagWriteMode.Both"/>.</summary>
        public string TagSeparator { get; set; } = ";";

        /// <summary>Create and use <c>journal_metadata</c> for deletes and highest sequence numbers.</summary>
        public bool DeleteCompatibilityMode { get; set; }

        /// <summary>Write the <c>writer_uuid</c> column. Turn off for tables created without it.</summary>
        public bool UseWriterUuidColumn { get; set; } = true;

        /// <summary>Name of the journal table.</summary>
        public string JournalTableName { get; set; } = "journal";

        /// <summary>Name of the tag table.</summary>
        public string TagTableName { get; set; } = "tags";

        /// <summary>Name of the metadata table.</summary>
        public string MetadataTableName { get; set; } = "journal_metadata";

        /// <summary>Write requests queued for the writer thread. Further writes fail.</summary>
        public int BufferSize { get; set; } = 5000;

        /// <summary>Rows per write transaction.</summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>Rows per round trip during recovery.</summary>
        public int ReplayBatchSize { get; set; } = 1000;

        /// <summary>Threads that run recovery reads.</summary>
        public int ReadThreads { get; set; } = 2;

        /// <inheritdoc />
        protected override Config InternalDefaultConfig => SqlitePersistence.DefaultJournalConfiguration;

        /// <inheritdoc />
        protected override Akka.Persistence.Hosting.PluginActorFactory? CreatePluginActorFactory()
            => Akka.Persistence.Hosting.PluginActorFactory.For(static config => new SqliteWriteJournal(config));

        /// <inheritdoc />
        protected override StringBuilder Build(StringBuilder sb)
        {
            sb.AppendLine($"connection-string = {HoconText.Quote(ConnectionString)}");
            sb.AppendLine($"tag-write-mode = {TagWriteMode}");
            sb.AppendLine($"tag-separator = {HoconText.Quote(TagSeparator)}");
            sb.AppendLine($"delete-compatibility-mode = {HoconText.Bool(DeleteCompatibilityMode)}");
            sb.AppendLine($"buffer-size = {BufferSize}");
            sb.AppendLine($"batch-size = {BatchSize}");
            sb.AppendLine($"replay-batch-size = {ReplayBatchSize}");
            sb.AppendLine($"read-threads = {ReadThreads}");
            sb.AppendLine($"default.journal.use-writer-uuid-column = {HoconText.Bool(UseWriterUuidColumn)}");
            sb.AppendLine($"default.journal.table-name = {HoconText.Quote(JournalTableName)}");
            sb.AppendLine($"default.tag.table-name = {HoconText.Quote(TagTableName)}");
            sb.AppendLine($"default.metadata.table-name = {HoconText.Quote(MetadataTableName)}");

            return base.Build(sb);
        }
    }

    /// <summary>Options of the SQLite snapshot store.</summary>
    public sealed class EmbeddedSnapshotOptions : Akka.Persistence.Hosting.SnapshotOptions
    {
        /// <summary>Creates options for the default snapshot store, <c>akka.persistence.snapshot-store.embedded</c>.</summary>
        public EmbeddedSnapshotOptions() : this(true)
        {
        }

        /// <summary>Creates options for a snapshot store registered under <paramref name="identifier"/>.</summary>
        public EmbeddedSnapshotOptions(bool isDefaultPlugin, string identifier = "embedded") : base(isDefaultPlugin)
        {
            Identifier = identifier;
            AutoInitialize = true;
        }

        /// <inheritdoc />
        public override string Identifier { get; set; }

        /// <summary>Microsoft.Data.Sqlite connection string. Required.</summary>
        public string ConnectionString { get; set; } = "";

        /// <summary>Name of the snapshot table.</summary>
        public string TableName { get; set; } = "snapshot";

        /// <inheritdoc />
        protected override Config InternalDefaultConfig => SqlitePersistence.DefaultSnapshotConfiguration;

        /// <inheritdoc />
        protected override Akka.Persistence.Hosting.PluginActorFactory? CreatePluginActorFactory()
            => Akka.Persistence.Hosting.PluginActorFactory.For(static config => new SqliteSnapshotStore(config));

        /// <inheritdoc />
        protected override StringBuilder Build(StringBuilder sb)
        {
            sb.AppendLine($"connection-string = {HoconText.Quote(ConnectionString)}");
            sb.AppendLine($"default.snapshot.table-name = {HoconText.Quote(TableName)}");

            return base.Build(sb);
        }
    }

    /// <summary>Options of the SQLite read journal.</summary>
    public sealed class EmbeddedReadJournalOptions
    {
        /// <summary>Creates options for the default read journal, <c>akka.persistence.query.journal.embedded</c>.</summary>
        public EmbeddedReadJournalOptions()
        {
        }

        /// <summary>Creates options for a read journal registered under <paramref name="identifier"/>.</summary>
        public EmbeddedReadJournalOptions(string identifier)
        {
            Identifier = identifier;
        }

        /// <summary>Identifier of the read journal: <c>akka.persistence.query.journal.{Identifier}</c>.</summary>
        public string Identifier { get; set; } = "embedded";

        /// <summary>Identifier of the journal this read journal reads: <c>akka.persistence.journal.{WriteJournalIdentifier}</c>.</summary>
        public string WriteJournalIdentifier { get; set; } = "embedded";

        /// <summary>The read journal's plugin id.</summary>
        public string PluginId => $"akka.persistence.query.journal.{Identifier}";

        /// <summary>Poll interval of live queries when the last batch was not full.</summary>
        public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Rows per query round trip.</summary>
        public int MaxBufferSize { get; set; } = 500;

        /// <summary>Bound live and by-tag batches with the Akka.Persistence.Sql style gap tracker. Off by default.</summary>
        public bool JournalSequenceRetrievalEnabled { get; set; }

        /// <summary>The plugin's reference configuration for the read journal.</summary>
        public Config DefaultConfig => SqlitePersistence.DefaultQueryConfiguration.MoveTo(PluginId);

        /// <summary>The settings as a <see cref="Config"/>.</summary>
        public Config ToConfig() => ToString();

        /// <inheritdoc />
        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Identifier))
                throw new InvalidOperationException($"Invalid {GetType()}, {nameof(Identifier)} is null or whitespace");

            var sb = new StringBuilder();
            sb.AppendLine($"{PluginId} {{");
            sb.AppendLine($"write-plugin = \"akka.persistence.journal.{WriteJournalIdentifier}\"");
            sb.AppendLine($"refresh-interval = {HoconText.Seconds(RefreshInterval)}");
            sb.AppendLine($"max-buffer-size = {MaxBufferSize}");
            sb.AppendLine($"journal-sequence-retrieval.enabled = {HoconText.Bool(JournalSequenceRetrievalEnabled)}");
            sb.AppendLine("}");
            return sb.ToString();
        }
    }
}
