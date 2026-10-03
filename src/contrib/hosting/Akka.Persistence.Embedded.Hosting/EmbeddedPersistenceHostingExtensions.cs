//-----------------------------------------------------------------------
// <copyright file="EmbeddedPersistenceHostingExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using Akka.Hosting;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Hosting;

namespace Akka.Persistence.Embedded.Hosting
{
    /// <summary>
    /// Adds the SQLite journal, snapshot store and read journal to an Akka.Hosting <see cref="AkkaConfigurationBuilder"/>.
    /// The plugins are registered in code, so they start with <c>Akka.DynamicTypeLoading</c> off (Native AOT) and need
    /// no HOCON <c>class</c> setting.
    /// </summary>
    public static class EmbeddedPersistenceHostingExtensions
    {
        /// <summary>
        /// Adds the journal, the snapshot store and the read journal (<paramref name="mode"/> limits the first two) in one call.
        /// Call it again with another <paramref name="pluginIdentifier"/> to add a second database.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <param name="connectionString">Microsoft.Data.Sqlite connection string, for example <c>Data Source=app.db</c>.</param>
        /// <param name="configureJournal">Event adapters and health checks of the journal.</param>
        /// <param name="mode">Which plugins to add. The read journal comes with the journal.</param>
        /// <param name="autoInitialize">Create missing tables on start.</param>
        /// <param name="tagWriteMode">Where tags are stored.</param>
        /// <param name="pluginIdentifier">Plugin identifier: <c>akka.persistence.journal.{id}</c>, <c>akka.persistence.snapshot-store.{id}</c> and <c>akka.persistence.query.journal.{id}</c>.</param>
        /// <param name="isDefaultPlugin">Make these the default journal and snapshot store.</param>
        /// <param name="configureSnapshot">Health checks of the snapshot store.</param>
        public static AkkaConfigurationBuilder WithEmbeddedPersistence(
            this AkkaConfigurationBuilder builder,
            string connectionString,
            Action<AkkaPersistenceJournalBuilder>? configureJournal = null,
            PersistenceMode mode = PersistenceMode.Both,
            bool autoInitialize = true,
            TagWriteMode tagWriteMode = TagWriteMode.TagTable,
            string pluginIdentifier = "embedded",
            bool isDefaultPlugin = true,
            Action<AkkaPersistenceSnapshotBuilder>? configureSnapshot = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException("A connection string is required.", nameof(connectionString));

            var includesJournal = mode is PersistenceMode.Both or PersistenceMode.Journal;
            var includesSnapshots = mode is PersistenceMode.Both or PersistenceMode.SnapshotStore;

            return builder.WithEmbeddedPersistence(
                includesJournal
                    ? new EmbeddedJournalOptions(isDefaultPlugin, pluginIdentifier)
                    {
                        ConnectionString = connectionString,
                        AutoInitialize = autoInitialize,
                        TagWriteMode = tagWriteMode
                    }
                    : null,
                includesSnapshots
                    ? new EmbeddedSnapshotOptions(isDefaultPlugin, pluginIdentifier)
                    {
                        ConnectionString = connectionString,
                        AutoInitialize = autoInitialize
                    }
                    : null,
                includesJournal ? new EmbeddedReadJournalOptions(pluginIdentifier) { WriteJournalIdentifier = pluginIdentifier } : null,
                configureJournal,
                configureSnapshot);
        }

        /// <summary>
        /// Adds the plugins whose options are given: any of the journal, the snapshot store and the read journal.
        /// </summary>
        /// <param name="builder">The builder.</param>
        /// <param name="journalOptions">The journal, or null for none.</param>
        /// <param name="snapshotOptions">The snapshot store, or null for none.</param>
        /// <param name="readJournalOptions">The read journal, or null for none. It reads <see cref="EmbeddedReadJournalOptions.WriteJournalIdentifier"/>.</param>
        /// <param name="configureJournal">Event adapters and health checks of the journal.</param>
        /// <param name="configureSnapshot">Health checks of the snapshot store.</param>
        public static AkkaConfigurationBuilder WithEmbeddedPersistence(
            this AkkaConfigurationBuilder builder,
            EmbeddedJournalOptions? journalOptions,
            EmbeddedSnapshotOptions? snapshotOptions,
            EmbeddedReadJournalOptions? readJournalOptions = null,
            Action<AkkaPersistenceJournalBuilder>? configureJournal = null,
            Action<AkkaPersistenceSnapshotBuilder>? configureSnapshot = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            if (journalOptions is not null)
                builder.WithJournal(journalOptions, configureJournal);

            if (snapshotOptions is not null)
                builder.WithSnapshot(snapshotOptions, configureSnapshot);

            if (readJournalOptions is not null)
                builder.WithEmbeddedReadJournal(readJournalOptions);

            return builder;
        }

        /// <summary>Adds the journal and its read journal, without a snapshot store.</summary>
        public static AkkaConfigurationBuilder WithEmbeddedJournal(
            this AkkaConfigurationBuilder builder,
            EmbeddedJournalOptions journalOptions,
            Action<AkkaPersistenceJournalBuilder>? configureJournal = null,
            EmbeddedReadJournalOptions? readJournalOptions = null)
        {
            if (journalOptions is null)
                throw new ArgumentNullException(nameof(journalOptions));

            return builder.WithEmbeddedPersistence(
                journalOptions,
                null,
                readJournalOptions ?? new EmbeddedReadJournalOptions(journalOptions.Identifier) { WriteJournalIdentifier = journalOptions.Identifier },
                configureJournal);
        }

        /// <summary>Adds the snapshot store only.</summary>
        public static AkkaConfigurationBuilder WithEmbeddedSnapshotStore(
            this AkkaConfigurationBuilder builder,
            EmbeddedSnapshotOptions snapshotOptions,
            Action<AkkaPersistenceSnapshotBuilder>? configureSnapshot = null)
        {
            if (snapshotOptions is null)
                throw new ArgumentNullException(nameof(snapshotOptions));

            return builder.WithEmbeddedPersistence(null, snapshotOptions, null, null, configureSnapshot);
        }

        /// <summary>
        /// Adds the read journal alone, for a journal that is configured elsewhere (for example with
        /// <see cref="AkkaPersistenceHostingExtensions.WithJournal(AkkaConfigurationBuilder, JournalOptions)"/>).
        /// </summary>
        public static AkkaConfigurationBuilder WithEmbeddedReadJournal(
            this AkkaConfigurationBuilder builder,
            EmbeddedReadJournalOptions? readJournalOptions = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            var options = readJournalOptions ?? new EmbeddedReadJournalOptions();
            var pluginId = options.PluginId;

            // HOCON first (the user's settings win), the plugin's reference config underneath
            builder.AddHocon(options.ToConfig(), HoconAddMode.Prepend);
            builder.AddHocon(options.DefaultConfig, HoconAddMode.Append);

            return builder.WithReadJournal(
                pluginId,
                (system, config) => new SqliteReadJournalProvider(system, config, pluginId),
                options.DefaultConfig.GetConfig(pluginId));
        }
    }
}
