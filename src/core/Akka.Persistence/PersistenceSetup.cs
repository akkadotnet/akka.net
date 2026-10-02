//-----------------------------------------------------------------------
// <copyright file="PersistenceSetup.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.Persistence.Journal;

namespace Akka.Persistence
{
    /// <summary>
    /// Registers persistence plugins in code, keyed by plugin id, so Akka.Persistence can build journals and
    /// snapshot stores (and, through Akka.Persistence.Query, read journals) without reflection. Required when
    /// the Akka.DynamicTypeLoading feature switch is off (Native AOT, trimmed apps); optional otherwise.
    /// <para>
    /// A plugin registered for <c>akka.persistence.journal.foo</c> is used when persistence starts the plugin
    /// at that path. Its HOCON <c>class</c> setting is not needed and is ignored. HOCON stays in charge of
    /// everything else in the section, and the registration's default config sits underneath it.
    /// </para>
    /// <para>
    /// Like <see cref="Akka.Serialization.SerializationSetup"/>, only one instance counts per
    /// <see cref="ActorSystemSetup"/>: a second one passed to <see cref="ActorSystemSetup.And{T}"/> replaces the
    /// first. Use <see cref="Merge"/> to combine setups.
    /// </para>
    /// </summary>
    public sealed class PersistenceSetup : Setup
    {
        private readonly ImmutableList<Func<ExtendedActorSystem, IEnumerable<PersistencePluginDetails>>> _sources;

        private PersistenceSetup(
            ImmutableList<Func<ExtendedActorSystem, IEnumerable<PersistencePluginDetails>>> sources,
            IStashOverflowStrategyConfigurator? stashOverflowConfigurator)
        {
            _sources = sources;
            StashOverflowConfigurator = stashOverflowConfigurator;
        }

        /// <summary>
        /// A setup with no registrations. Add plugins with the <c>With...</c> methods.
        /// </summary>
        public static PersistenceSetup Create()
            => new(ImmutableList<Func<ExtendedActorSystem, IEnumerable<PersistencePluginDetails>>>.Empty, null);

        /// <summary>
        /// A setup whose plugins come from a factory that runs once when the <see cref="ActorSystem"/> starts,
        /// the way <see cref="Akka.Serialization.SerializationSetup.Create"/> works. Plugin packages use this form.
        /// </summary>
        /// <param name="createPlugins">Returns the plugin records, one per plugin id.</param>
        public static PersistenceSetup Create(Func<ExtendedActorSystem, ImmutableHashSet<PersistencePluginDetails>> createPlugins)
            => Create().WithPlugins(createPlugins);

        /// <summary>
        /// Runs every registration and returns the plugin records. On a repeated plugin id the later
        /// registration wins.
        /// </summary>
        public Func<ExtendedActorSystem, ImmutableHashSet<PersistencePluginDetails>> CreatePlugins => system =>
        {
            var byId = new Dictionary<string, PersistencePluginDetails>(StringComparer.Ordinal);
            foreach (var source in _sources)
            {
                foreach (var details in source(system))
                    byId[details.PluginId] = details;
            }

            return byId.Values.ToImmutableHashSet();
        };

        /// <summary>
        /// The configurator that replaces <c>akka.persistence.internal-stash-overflow-strategy</c>, if one was set.
        /// </summary>
        internal IStashOverflowStrategyConfigurator? StashOverflowConfigurator { get; }

        /// <summary>
        /// Adds plugin records from a factory. Runs once when the <see cref="ActorSystem"/> starts.
        /// </summary>
        /// <param name="createPlugins">Returns the plugin records.</param>
        /// <returns>A new setup that also holds these plugins.</returns>
        public PersistenceSetup WithPlugins(Func<ExtendedActorSystem, ImmutableHashSet<PersistencePluginDetails>> createPlugins)
        {
            if (createPlugins is null)
                throw new ArgumentNullException(nameof(createPlugins));

            return new PersistenceSetup(_sources.Add(createPlugins), StashOverflowConfigurator);
        }

        /// <summary>
        /// Adds one plugin record. On the same plugin id the new record replaces the old one.
        /// </summary>
        /// <param name="details">The plugin record, for example from <see cref="JournalDetails.Create{TJournal}"/>.</param>
        /// <returns>A new setup that also holds this plugin.</returns>
        public PersistenceSetup WithPlugin(PersistencePluginDetails details)
        {
            if (details is null)
                throw new ArgumentNullException(nameof(details));

            return new PersistenceSetup(_sources.Add(_ => new[] { details }), StashOverflowConfigurator);
        }

        /// <summary>
        /// Registers a journal for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TJournal">The journal actor type.</typeparam>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.journal.my-journal</c>.</param>
        /// <param name="factory">Creates the journal inside its actor context, given the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <param name="eventAdapters">The event adapters of this journal, each with the event types it is bound to.</param>
        /// <returns>A new setup that also holds this journal.</returns>
        public PersistenceSetup WithJournal<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TJournal>(
            string pluginId,
            Func<Config, TJournal> factory,
            Config? defaultConfig = null,
            IEnumerable<EventAdapterDetails>? eventAdapters = null) where TJournal : ActorBase
            => WithPlugin(JournalDetails.Create(pluginId, factory, defaultConfig, eventAdapters));

        /// <summary>
        /// Registers a snapshot store for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TStore">The snapshot store actor type.</typeparam>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.snapshot-store.my-store</c>.</param>
        /// <param name="factory">Creates the snapshot store inside its actor context, given the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <returns>A new setup that also holds this snapshot store.</returns>
        public PersistenceSetup WithSnapshotStore<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TStore>(
            string pluginId,
            Func<Config, TStore> factory,
            Config? defaultConfig = null) where TStore : ActorBase
            => WithPlugin(SnapshotStoreDetails.Create(pluginId, factory, defaultConfig));

        /// <summary>
        /// Replaces <c>akka.persistence.internal-stash-overflow-strategy</c> with this configurator.
        /// </summary>
        /// <param name="configurator">The configurator to use.</param>
        /// <returns>A new setup with this configurator.</returns>
        public PersistenceSetup WithStashOverflowStrategy(IStashOverflowStrategyConfigurator configurator)
        {
            if (configurator is null)
                throw new ArgumentNullException(nameof(configurator));

            return new PersistenceSetup(_sources, configurator);
        }

        /// <summary>
        /// Returns a setup with the registrations of both. On the same plugin id, <paramref name="other"/> wins,
        /// and so does its stash overflow configurator if it has one.
        /// </summary>
        /// <param name="other">The setup to merge on top of this one.</param>
        /// <returns>The combined setup.</returns>
        public PersistenceSetup Merge(PersistenceSetup other)
        {
            if (other is null)
                throw new ArgumentNullException(nameof(other));

            return new PersistenceSetup(_sources.AddRange(other._sources), other.StashOverflowConfigurator ?? StashOverflowConfigurator);
        }
    }

    /// <summary>
    /// A persistence plugin registered in code: its plugin id and the config that sits under its HOCON section.
    /// Two records are equal when their <see cref="PluginId"/> is. Use <see cref="JournalDetails"/> and
    /// <see cref="SnapshotStoreDetails"/>; Akka.Persistence.Query adds one for read journals.
    /// </summary>
    public abstract class PersistencePluginDetails : IEquatable<PersistencePluginDetails>
    {
        /// <summary>
        /// Creates a record for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <param name="pluginId">The plugin's config path.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, or <c>null</c>.</param>
        protected PersistencePluginDetails(string pluginId, Config? defaultConfig)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("A plugin id is required.", nameof(pluginId));

            PluginId = pluginId;
            DefaultConfig = defaultConfig;
        }

        /// <summary>
        /// The plugin's config path, for example <c>akka.persistence.journal.my-journal</c>.
        /// </summary>
        public string PluginId { get; }

        /// <summary>
        /// Config merged under the plugin's HOCON section: HOCON wins, this fills the gaps.
        /// </summary>
        public Config? DefaultConfig { get; }

        /// <inheritdoc />
        public bool Equals(PersistencePluginDetails? other)
            => other is not null && string.Equals(PluginId, other.PluginId, StringComparison.Ordinal);

        /// <inheritdoc />
        public override bool Equals(object? obj) => Equals(obj as PersistencePluginDetails);

        /// <inheritdoc />
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(PluginId);
    }

    /// <summary>
    /// A journal registered in code. Create one with <see cref="Create{TJournal}"/> or
    /// <see cref="PersistenceSetup.WithJournal{TJournal}"/>.
    /// </summary>
    public sealed class JournalDetails : PersistencePluginDetails
    {
        private readonly Func<Config, Props> _createProps;

        private JournalDetails(string pluginId, Func<Config, Props> createProps, Config? defaultConfig, ImmutableArray<EventAdapterDetails> eventAdapters)
            : base(pluginId, defaultConfig)
        {
            _createProps = createProps;
            EventAdapters = eventAdapters;
        }

        /// <summary>
        /// The event adapters of this journal. They add to the journal's HOCON <c>event-adapters</c>
        /// and win on a name clash.
        /// </summary>
        public IReadOnlyList<EventAdapterDetails> EventAdapters { get; }

        /// <summary>
        /// Registers a journal for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TJournal">The journal actor type.</typeparam>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.journal.my-journal</c>.</param>
        /// <param name="factory">Creates the journal inside its actor context, given the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <param name="eventAdapters">The event adapters of this journal, each with the event types it is bound to.</param>
        /// <returns>The record.</returns>
        public static JournalDetails Create<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TJournal>(
            string pluginId,
            Func<Config, TJournal> factory,
            Config? defaultConfig = null,
            IEnumerable<EventAdapterDetails>? eventAdapters = null) where TJournal : ActorBase
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            var adapters = (eventAdapters ?? Enumerable.Empty<EventAdapterDetails>()).ToImmutableArray();
            var duplicate = adapters.GroupBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                throw new ArgumentException($"Event adapter name [{duplicate.Key}] is used more than once.", nameof(eventAdapters));

            // the closed generic producer is created here, where the type is known, so the trimmer keeps what Props needs
            return new JournalDetails(pluginId, config => Props.CreateBy(new PluginActorProducer<TJournal>(factory, config)), defaultConfig, adapters);
        }

        internal Props CreateProps(Config config) => _createProps(config);
    }

    /// <summary>
    /// A snapshot store registered in code. Create one with <see cref="Create{TStore}"/> or
    /// <see cref="PersistenceSetup.WithSnapshotStore{TStore}"/>.
    /// </summary>
    public sealed class SnapshotStoreDetails : PersistencePluginDetails
    {
        private readonly Func<Config, Props> _createProps;

        private SnapshotStoreDetails(string pluginId, Func<Config, Props> createProps, Config? defaultConfig)
            : base(pluginId, defaultConfig)
        {
            _createProps = createProps;
        }

        /// <summary>
        /// Registers a snapshot store for the plugin at <paramref name="pluginId"/>.
        /// </summary>
        /// <typeparam name="TStore">The snapshot store actor type.</typeparam>
        /// <param name="pluginId">The plugin's config path, for example <c>akka.persistence.snapshot-store.my-store</c>.</param>
        /// <param name="factory">Creates the snapshot store inside its actor context, given the plugin's config section.</param>
        /// <param name="defaultConfig">Config that sits under the plugin's section, so the app need not repeat it in HOCON.</param>
        /// <returns>The record.</returns>
        public static SnapshotStoreDetails Create<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TStore>(
            string pluginId,
            Func<Config, TStore> factory,
            Config? defaultConfig = null) where TStore : ActorBase
        {
            if (factory is null)
                throw new ArgumentNullException(nameof(factory));

            return new SnapshotStoreDetails(pluginId, config => Props.CreateBy(new PluginActorProducer<TStore>(factory, config)), defaultConfig);
        }

        internal Props CreateProps(Config config) => _createProps(config);
    }
}
