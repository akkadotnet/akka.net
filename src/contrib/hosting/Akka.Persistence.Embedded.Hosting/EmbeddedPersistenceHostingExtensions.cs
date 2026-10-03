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
    /// The methods mirror <c>WithSqlPersistence</c> of Akka.Persistence.Sql.Hosting. The plugins are registered in code,
    /// so they start with <c>Akka.DynamicTypeLoading</c> off (Native AOT) and need no HOCON <c>class</c> setting.
    /// The read journal comes with the journal, and its settings are properties of <see cref="EmbeddedJournalOptions"/>.
    /// </summary>
    public static class EmbeddedPersistenceHostingExtensions
    {
        /// <summary>
        /// Adds the journal, the snapshot store and the read journal (<paramref name="mode"/> limits the first two) in one call.
        /// Call it again with another <paramref name="pluginIdentifier"/> to add a second database.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="connectionString">Microsoft.Data.Sqlite connection string, for example <c>Data Source=app.db</c>.</param>
        /// <param name="mode">Which plugins to add. The read journal comes with the journal. Default <see cref="PersistenceMode.Both"/>.</param>
        /// <param name="journalBuilder">Event adapters and health checks of the journal.</param>
        /// <param name="snapshotBuilder">Health checks of the snapshot store.</param>
        /// <param name="autoInitialize">Create missing tables on start. Default <c>true</c>.</param>
        /// <param name="pluginIdentifier">Plugin identifier: <c>akka.persistence.journal.{id}</c>, <c>akka.persistence.snapshot-store.{id}</c> and <c>akka.persistence.query.journal.{id}</c>. Default <c>"embedded"</c>.</param>
        /// <param name="isDefaultPlugin">Make these the default journal and snapshot store. Default <c>true</c>.</param>
        /// <param name="tagStorageMode">Where tags are stored. Leave null for <see cref="TagWriteMode.TagTable"/>.</param>
        /// <param name="deleteCompatibilityMode">If true, <c>journal_metadata</c> is created and used for deletes and highest sequence numbers.</param>
        /// <param name="useWriterUuidColumn">Write the <c>writer_uuid</c> column. Turn off for tables created without it.</param>
        /// <param name="maxConcurrentQueries">How many read journal queries run or wait at once.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="Exception">
        /// Thrown when a builder is given for a plugin that <paramref name="mode"/> leaves out.
        /// </exception>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="connectionString"/> is null or white space.</exception>
        public static AkkaConfigurationBuilder WithEmbeddedPersistence(
            this AkkaConfigurationBuilder builder,
            string connectionString,
            PersistenceMode mode = PersistenceMode.Both,
            Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
            Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
            bool autoInitialize = true,
            string pluginIdentifier = "embedded",
            bool isDefaultPlugin = true,
            TagWriteMode? tagStorageMode = null,
            bool? deleteCompatibilityMode = null,
            bool? useWriterUuidColumn = null,
            int? maxConcurrentQueries = null)
        {
            if (mode == PersistenceMode.SnapshotStore && journalBuilder is not null)
                throw new Exception($"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

            if (mode == PersistenceMode.Journal && snapshotBuilder is not null)
                throw new Exception($"{nameof(snapshotBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.SnapshotStore}");

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentNullException(nameof(connectionString), $"{nameof(connectionString)} can not be null");

            var journalOpt = new EmbeddedJournalOptions(isDefaultPlugin, pluginIdentifier)
            {
                ConnectionString = connectionString,
                AutoInitialize = autoInitialize,
                TagStorageMode = tagStorageMode,
                DeleteCompatibilityMode = deleteCompatibilityMode,
                UseWriterUuidColumn = useWriterUuidColumn,
                MaxConcurrentQueries = maxConcurrentQueries,
            };

            var snapshotOpt = new EmbeddedSnapshotOptions(isDefaultPlugin, pluginIdentifier)
            {
                ConnectionString = connectionString,
                AutoInitialize = autoInitialize,
            };

            return mode switch
            {
                PersistenceMode.Journal => builder.WithEmbeddedPersistence(journalOpt, null, journalBuilder, snapshotBuilder),
                PersistenceMode.SnapshotStore => builder.WithEmbeddedPersistence(null, snapshotOpt, journalBuilder, snapshotBuilder),
                PersistenceMode.Both => builder.WithEmbeddedPersistence(journalOpt, snapshotOpt, journalBuilder, snapshotBuilder),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined."),
            };
        }

        /// <summary>
        /// Adds the journal, the snapshot store and the read journal. At least one of the configurator delegates
        /// needs to be populated.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptionConfigurator">Sets the properties of <see cref="EmbeddedJournalOptions"/>. Leave null for no journal.</param>
        /// <param name="snapshotOptionConfigurator">Sets the properties of <see cref="EmbeddedSnapshotOptions"/>. Leave null for no snapshot store.</param>
        /// <param name="isDefaultPlugin">Make these the default journal and snapshot store. Default <c>true</c>.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentException">Thrown when both delegates are null.</exception>
        public static AkkaConfigurationBuilder WithEmbeddedPersistence(
            this AkkaConfigurationBuilder builder,
            Action<EmbeddedJournalOptions>? journalOptionConfigurator = null,
            Action<EmbeddedSnapshotOptions>? snapshotOptionConfigurator = null,
            bool isDefaultPlugin = true)
        {
            if (journalOptionConfigurator is null && snapshotOptionConfigurator is null)
                throw new ArgumentException($"{nameof(journalOptionConfigurator)} and {nameof(snapshotOptionConfigurator)} could not both be null");

            EmbeddedJournalOptions? journalOptions = null;
            if (journalOptionConfigurator is not null)
            {
                journalOptions = new EmbeddedJournalOptions(isDefaultPlugin);
                journalOptionConfigurator(journalOptions);
            }

            EmbeddedSnapshotOptions? snapshotOptions = null;
            if (snapshotOptionConfigurator is not null)
            {
                snapshotOptions = new EmbeddedSnapshotOptions(isDefaultPlugin);
                snapshotOptionConfigurator(snapshotOptions);
            }

            return builder.WithEmbeddedPersistence(journalOptions, snapshotOptions);
        }

        /// <summary>
        /// Adds the journal, the snapshot store and the read journal from their options. At least one of the options
        /// has to be populated. The read journal reads the journal and takes its <c>Query*</c> settings from
        /// <paramref name="journalOptions"/>, so there is nothing else to register.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The journal, or null for none.</param>
        /// <param name="snapshotOptions">The snapshot store, or null for none.</param>
        /// <param name="journalBuilder">Event adapters and health checks of the journal.</param>
        /// <param name="snapshotBuilder">Health checks of the snapshot store.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentException">Thrown when both options are null.</exception>
        public static AkkaConfigurationBuilder WithEmbeddedPersistence(
            this AkkaConfigurationBuilder builder,
            EmbeddedJournalOptions? journalOptions = null,
            EmbeddedSnapshotOptions? snapshotOptions = null,
            Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
            Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));

            if (journalOptions is null && snapshotOptions is null)
                throw new ArgumentException($"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null");

            if (journalOptions is not null)
            {
                builder.WithJournal(journalOptions, journalBuilder);
                builder.AddReadJournal(journalOptions.QueryPluginId, journalOptions);

                // a default journal under another id is also what the default read journal id reads
                if (journalOptions.IsDefaultPlugin && journalOptions.QueryPluginId != SqlitePersistence.QueryPluginId)
                    builder.AddReadJournal(SqlitePersistence.QueryPluginId, journalOptions);
            }

            if (snapshotOptions is not null)
                builder.WithSnapshot(snapshotOptions, snapshotBuilder);

            return builder;
        }

        private static void AddReadJournal(this AkkaConfigurationBuilder builder, string queryPluginId, EmbeddedJournalOptions journalOptions)
        {
            // the plugin's reference section sits under the options' HOCON, which the journal options have already added
            var defaults = SqlitePersistence.DefaultQueryConfiguration.MoveTo(queryPluginId);
            builder.AddHocon(defaults, HoconAddMode.Append);

            builder.WithReadJournal(
                queryPluginId,
                (system, config) => new SqliteReadJournalProvider(system, config, queryPluginId),
                SqlitePersistence.DefaultQueryConfiguration);
        }
    }
}
