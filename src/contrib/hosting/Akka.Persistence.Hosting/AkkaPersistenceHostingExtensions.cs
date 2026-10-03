using System;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Actor;

#nullable enable
namespace Akka.Persistence.Hosting
{
    public enum PersistenceMode
    {
        /// <summary>
        /// Sets both the akka.persistence.journal and the akka.persistence.snapshot-store to use this plugin.
        /// </summary>
        Both,

        /// <summary>
        /// Sets ONLY the akka.persistence.journal to use this plugin.
        /// </summary>
        Journal,

        /// <summary>
        /// Sets ONLY the akka.persistence.snapshot-store to use this plugin.
        /// </summary>
        SnapshotStore,
    }

    /// <summary>
    /// The set of options for generic Akka.Persistence.
    /// </summary>
    public static class AkkaPersistenceHostingExtensions
    {
        /// <summary>
        /// A generic way to add both journal and snapshot store configuration to the <see cref="ActorSystem"/>
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The specific journal options instance used to configure the journal. For example, an instance of <c>SqlServerJournalOptions</c></param>
        /// <param name="snapshotOptions">The specific snapshot store options instance used to configure the snapshot store. For example, an instance of <c>SqlServerSnapshotOptions</c></param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static AkkaConfigurationBuilder WithJournalAndSnapshot(
            this AkkaConfigurationBuilder builder,
            JournalOptions journalOptions,
            SnapshotOptions snapshotOptions)
            => WithJournalAndSnapshot(builder, journalOptions, snapshotOptions,
                configureJournal: null, configureSnapshot: null);

        /// <summary>
        /// A generic way to add both journal and snapshot store configuration to the <see cref="ActorSystem"/> with support for event adapters and health checks.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The specific journal options instance used to configure the journal. For example, an instance of <c>SqlServerJournalOptions</c></param>
        /// <param name="snapshotOptions">The specific snapshot store options instance used to configure the snapshot store. For example, an instance of <c>SqlServerSnapshotOptions</c></param>
        /// <param name="configureJournal">Optional action to configure event adapters and health checks for the journal.</param>
        /// <param name="configureSnapshot">Optional action to configure health checks for the snapshot store.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <example>
        /// <code>
        /// builder.WithJournalAndSnapshot(
        ///     new SqlServerJournalOptions
        ///     {
        ///         ConnectionString = "...",
        ///         IsDefaultPlugin = true
        ///     },
        ///     new SqlServerSnapshotOptions
        ///     {
        ///         ConnectionString = "...",
        ///         IsDefaultPlugin = true
        ///     },
        ///     journal => journal
        ///         .AddWriteEventAdapter&lt;MyAdapter&gt;("adapter", new[] { typeof(MyEvent) })
        ///         .WithHealthCheck(HealthStatus.Degraded),
        ///     snapshot => snapshot
        ///         .WithHealthCheck());
        /// </code>
        /// </example>
        public static AkkaConfigurationBuilder WithJournalAndSnapshot(
            this AkkaConfigurationBuilder builder,
            JournalOptions journalOptions,
            SnapshotOptions snapshotOptions,
            Action<AkkaPersistenceJournalBuilder>? configureJournal,
            Action<AkkaPersistenceSnapshotBuilder>? configureSnapshot)
        {


            return builder.WithJournal(journalOptions, configureJournal)
                .WithSnapshot(snapshotOptions, configureSnapshot);
        }

        /// <summary>
        /// A generic way to add journal configuration to the <see cref="ActorSystem"/>
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The specific journal options instance used to configure the journal. For example, an instance of <c>SqlServerJournalOptions</c></param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static AkkaConfigurationBuilder WithJournal(
            this AkkaConfigurationBuilder builder,
            JournalOptions journalOptions)
            => WithJournal(builder, journalOptions, configureBuilder: null);

        /// <summary>
        /// A generic way to add journal configuration to the <see cref="ActorSystem"/> with support for event adapters and health checks.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The specific journal options instance used to configure the journal. For example, an instance of <c>SqlServerJournalOptions</c></param>
        /// <param name="configureBuilder">Optional action to configure event adapters and health checks for this journal.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <example>
        /// <code>
        /// builder.WithJournal(
        ///     new SqlServerJournalOptions
        ///     {
        ///         ConnectionString = "...",
        ///         IsDefaultPlugin = true
        ///     },
        ///     journal => journal
        ///         .AddWriteEventAdapter&lt;MyAdapter&gt;("adapter", new[] { typeof(MyEvent) })
        ///         .WithHealthCheck(HealthStatus.Degraded));
        /// </code>
        /// </example>
        public static AkkaConfigurationBuilder WithJournal(
            this AkkaConfigurationBuilder builder,
            JournalOptions journalOptions,
            Action<AkkaPersistenceJournalBuilder>? configureBuilder)
        {
            if (journalOptions is null)
                throw new ArgumentNullException(nameof(journalOptions));

            // Apply the options configuration
            builder.AddHocon(journalOptions.ToConfig(), HoconAddMode.Prepend);
            builder.AddHocon(journalOptions.DefaultConfig, HoconAddMode.Append);

            // a plugin that supplies a factory starts without its HOCON `class`, which is what Native AOT needs
            if (journalOptions.GetFactory() is { } factory)
            {
                var details = JournalDetails.FromProps(journalOptions.PluginId, factory.CreateProps);
                builder.AddPersistenceRegistrations(setup => setup.WithPlugin(details));
            }

            // Apply the builder configuration (adapters + health checks) if provided
            if (configureBuilder != null)
            {
                var jBuilder = new AkkaPersistenceJournalBuilder(journalOptions.Identifier, builder, journalOptions);
                configureBuilder(jBuilder);
                jBuilder.Build();
            }

            return builder;
        }

        /// <summary>
        /// A generic way to add snapshot store configuration to the <see cref="ActorSystem"/>
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="snapshotOptions">The specific snapshot store options instance used to configure the snapshot store. For example, an instance of <c>SqlServerSnapshotOptions</c></param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static AkkaConfigurationBuilder WithSnapshot(
            this AkkaConfigurationBuilder builder,
            SnapshotOptions snapshotOptions)
            => WithSnapshot(builder, snapshotOptions, configureBuilder: null);

        /// <summary>
        /// A generic way to add snapshot store configuration to the <see cref="ActorSystem"/> with support for health checks.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="snapshotOptions">The specific snapshot store options instance used to configure the snapshot store. For example, an instance of <c>SqlServerSnapshotOptions</c></param>
        /// <param name="configureBuilder">Optional action to configure health checks for this snapshot store.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <example>
        /// <code>
        /// builder.WithSnapshot(
        ///     new SqlServerSnapshotOptions
        ///     {
        ///         ConnectionString = "...",
        ///         IsDefaultPlugin = true
        ///     },
        ///     snapshot => snapshot
        ///         .WithHealthCheck(HealthStatus.Degraded));
        /// </code>
        /// </example>
        public static AkkaConfigurationBuilder WithSnapshot(
            this AkkaConfigurationBuilder builder,
            SnapshotOptions snapshotOptions,
            Action<AkkaPersistenceSnapshotBuilder>? configureBuilder)
        {
            if (snapshotOptions is null)
                throw new ArgumentNullException(nameof(snapshotOptions));

            // Apply the options configuration
            builder.AddHocon(snapshotOptions.ToConfig(), HoconAddMode.Prepend);
            builder.AddHocon(snapshotOptions.DefaultConfig, HoconAddMode.Append);

            // a plugin that supplies a factory starts without its HOCON `class`, which is what Native AOT needs
            if (snapshotOptions.GetFactory() is { } factory)
            {
                var details = SnapshotStoreDetails.FromProps(snapshotOptions.PluginId, factory.CreateProps);
                builder.AddPersistenceRegistrations(setup => setup.WithPlugin(details));
            }

            // Apply the builder configuration (health checks) if provided
            if (configureBuilder != null)
            {
                var sBuilder = new AkkaPersistenceSnapshotBuilder(snapshotOptions.Identifier, builder, snapshotOptions);
                configureBuilder(sBuilder);
                sBuilder.Build();
            }

            return builder;
        }

        /// <summary>
        /// Used to configure a specific Akka.Persistence.Journal instance, primarily to support <see cref="IEventAdapter"/>s.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalId">The id of the journal. i.e. if you want to apply this adapter to the `akka.persistence.journal.sql-server` journal, just type `sql-server`.</param>
        /// <param name="journalBuilder">Configuration method for configuring the journal.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        /// <remarks>
        /// This method can be called multiple times for different <see cref="IEventAdapter"/>s.
        /// </remarks>
        [Obsolete("Use WithJournal(journalOptions, configureBuilder) instead to combine options configuration with event adapters and health checks. This method will be removed in v1.6.")]
        public static AkkaConfigurationBuilder WithJournal(
            this AkkaConfigurationBuilder builder,
            string journalId,
            Action<AkkaPersistenceJournalBuilder> journalBuilder)
        {
            var jBuilder = new AkkaPersistenceJournalBuilder(journalId, builder);
            journalBuilder(jBuilder);

            // build and inject the HOCON
            jBuilder.Build();
            return builder;
        }

        public static AkkaConfigurationBuilder WithInMemoryJournal(this AkkaConfigurationBuilder builder)
        {
            return WithInMemoryJournal(builder, _ => { });
        }

        public static AkkaConfigurationBuilder WithInMemoryJournal(
            this AkkaConfigurationBuilder builder,
            Action<AkkaPersistenceJournalBuilder> journalBuilder,
            string journalId = "inmem",
            bool isDefaultPlugin = true)
        {
            
            var jBuilder = new AkkaPersistenceJournalBuilder(journalId, builder);
            journalBuilder(jBuilder);

            // build and inject the HOCON
            jBuilder.Build();

            var liveConfig =
                $$"""
                  {{(isDefaultPlugin ? $"akka.persistence.journal.plugin = akka.persistence.journal.{journalId}" : "")}}
                  akka.persistence.journal.{{journalId}} {
                      class = "Akka.Persistence.Journal.MemoryJournal, Akka.Persistence"
                      plugin-dispatcher = "akka.actor.default-dispatcher"
                  }
                  """;

            return builder.AddHocon(liveConfig, HoconAddMode.Prepend);
        }

        public static AkkaConfigurationBuilder WithInMemorySnapshotStore(
            this AkkaConfigurationBuilder builder,
            string snapshotStoreId = "inmem",
            bool isDefaultPlugin = true)
        {
            var liveConfig =
                $$"""
                  {{(isDefaultPlugin ? $"akka.persistence.snapshot-store.plugin = akka.persistence.snapshot-store.{snapshotStoreId}" : "")}}
                  akka.persistence.snapshot-store.{{snapshotStoreId}} {
                      class = "Akka.Persistence.Snapshot.MemorySnapshotStore, Akka.Persistence"
                      plugin-dispatcher = "akka.actor.default-dispatcher"
                  }
                  """;

            return builder.AddHocon(liveConfig, HoconAddMode.Prepend);
        }

        /// <summary>
        /// Registers a read journal, which <c>PersistenceQuery</c> then creates without reflection and without a HOCON
        /// <c>class</c> setting. A plugin's Hosting package calls this from its own query extension method.
        /// </summary>
        /// <typeparam name="TProvider">The read journal provider type.</typeparam>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="pluginId">The read journal's config path, for example <c>akka.persistence.query.journal.sql</c>.</param>
        /// <param name="factory">Creates the provider from the actor system and the read journal's HOCON section.</param>
        /// <param name="defaultConfig">The plugin's default section, which sits under the HOCON. <c>PersistenceQuery</c>
        /// would add it by reflection on the JIT; with <c>Akka.DynamicTypeLoading</c> off nothing else does.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithReadJournal<TProvider>(
            this AkkaConfigurationBuilder builder,
            string pluginId,
            Func<ExtendedActorSystem, Config, TProvider> factory,
            Config? defaultConfig = null) where TProvider : class, IReadJournalProvider
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            var details = ReadJournalDetails.Create(pluginId, factory, defaultConfig);
            return builder.AddPersistenceRegistrations(setup => setup.WithPlugin(details));
        }

        /// <summary>
        /// Replaces <c>akka.persistence.internal-stash-overflow-strategy</c> with this configurator, created without reflection.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="configurator">The configurator to use.</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithStashOverflowStrategy(
            this AkkaConfigurationBuilder builder,
            IStashOverflowStrategyConfigurator configurator)
        {
            if (builder is null)
                throw new ArgumentNullException(nameof(builder));
            if (configurator is null)
                throw new ArgumentNullException(nameof(configurator));

            return builder.AddPersistenceRegistrations(setup => setup.WithStashOverflowStrategy(configurator));
        }

        /// <summary>
        /// Adds the Akka.NET v1.4 to v1.5 Akka.Cluster.Sharding persistence event migration adapter to a journal.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalOptions">The specific journal options instance used by Akka.Cluster.Sharding persistence. For example, an instance of <c>SqlServerJournalOptions</c></param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithClusterShardingJournalMigrationAdapter(
            this AkkaConfigurationBuilder builder,
            JournalOptions journalOptions)
            => builder.WithClusterShardingJournalMigrationAdapter(journalOptions.PluginId);

        /// <summary>
        /// Adds the Akka.NET v1.4 to v1.5 Akka.Cluster.Sharding persistence event migration adapter to a journal.
        /// </summary>
        /// <param name="builder">The builder instance being configured.</param>
        /// <param name="journalId">The specific journal identifier used by Akka.Cluster.Sharding persistence. For example, "akka.persistence.journal.sql-server"</param>
        /// <returns>The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.</returns>
        public static AkkaConfigurationBuilder WithClusterShardingJournalMigrationAdapter(
            this AkkaConfigurationBuilder builder,
            string journalId)
        {
            var config = $$"""
                           {{journalId}} {
                                event-adapters {
                                   coordinator-migration = "Akka.Cluster.Sharding.OldCoordinatorStateMigrationEventAdapter, Akka.Cluster.Sharding"
                               }

                               event-adapter-bindings {
                                   "Akka.Cluster.Sharding.ShardCoordinator+IDomainEvent, Akka.Cluster.Sharding" = coordinator-migration
                               }
                           }
                           """;
            return builder.AddHocon(config, HoconAddMode.Prepend);
        }
    }
}