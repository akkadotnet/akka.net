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
    /// INTERNAL API
    ///
    /// An ordered list of persistence plugin registrations that Akka.Persistence.Hosting builds so that plugins,
    /// event adapters and read journals start without reflection (Native AOT, trimmed apps). Users never write
    /// one: they call the Akka.Persistence.Hosting builders, and each call appends a registration.
    /// <para>
    /// The registry merges the list, see <see cref="PersistencePluginRegistry"/>. Per plugin id the later
    /// registration wins the factory and the default config. Event adapters for a journal accumulate across
    /// registrations, and the later one wins an adapter-name clash.
    /// </para>
    /// <para>
    /// Like any <see cref="Setup"/>, only one instance counts per <see cref="ActorSystemSetup"/>, so
    /// Akka.Persistence.Hosting keeps a single one and replaces it with the <c>With...</c> result as calls come in.
    /// </para>
    /// </summary>
    internal sealed class PersistenceSetup : Setup
    {
        private PersistenceSetup(ImmutableList<PersistenceRegistration> registrations)
        {
            Registrations = registrations;
        }

        /// <summary>
        /// A setup with no registrations.
        /// </summary>
        public static PersistenceSetup Create() => new(ImmutableList<PersistenceRegistration>.Empty);

        /// <summary>
        /// The registrations in the order they were added.
        /// </summary>
        public ImmutableList<PersistenceRegistration> Registrations { get; }

        /// <summary>
        /// Appends a plugin registration.
        /// </summary>
        public PersistenceSetup WithPlugin(PersistencePluginDetails details)
        {
            if (details is null)
                throw new ArgumentNullException(nameof(details));

            return new PersistenceSetup(Registrations.Add(PersistenceRegistration.ForPlugin(details)));
        }

        /// <summary>
        /// Appends event adapters for the journal at <paramref name="journalPluginId"/>, whichever way that journal
        /// is registered: by a <see cref="JournalDetails"/>, as a built-in such as the in-memory journal, or in HOCON.
        /// </summary>
        public PersistenceSetup WithEventAdapters(string journalPluginId, IEnumerable<EventAdapterDetails> eventAdapters)
        {
            if (string.IsNullOrWhiteSpace(journalPluginId))
                throw new ArgumentException("A plugin id is required.", nameof(journalPluginId));
            if (eventAdapters is null)
                throw new ArgumentNullException(nameof(eventAdapters));

            return new PersistenceSetup(
                Registrations.AddRange(eventAdapters.Select(a => PersistenceRegistration.ForEventAdapter(journalPluginId, a))));
        }

        /// <summary>
        /// Appends a journal registration.
        /// </summary>
        public PersistenceSetup WithJournal<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TJournal>(
            string pluginId,
            Func<Config, TJournal> factory,
            Config? defaultConfig = null,
            IEnumerable<EventAdapterDetails>? eventAdapters = null) where TJournal : ActorBase
            => WithPlugin(JournalDetails.Create(pluginId, factory, defaultConfig, eventAdapters));

        /// <summary>
        /// Appends a snapshot store registration.
        /// </summary>
        public PersistenceSetup WithSnapshotStore<
            [DynamicallyAccessedMembers(Props.ActorTypeMembers)] TStore>(
            string pluginId,
            Func<Config, TStore> factory,
            Config? defaultConfig = null) where TStore : ActorBase
            => WithPlugin(SnapshotStoreDetails.Create(pluginId, factory, defaultConfig));

    }

    /// <summary>
    /// INTERNAL API
    ///
    /// One entry in a <see cref="PersistenceSetup"/>: a plugin, or an event adapter for a journal.
    /// </summary>
    internal sealed class PersistenceRegistration
    {
        private PersistenceRegistration(PersistencePluginDetails? plugin, string? journalPluginId, EventAdapterDetails? eventAdapter)
        {
            Plugin = plugin;
            JournalPluginId = journalPluginId;
            EventAdapter = eventAdapter;
        }

        public static PersistenceRegistration ForPlugin(PersistencePluginDetails plugin) => new(plugin, null, null);

        public static PersistenceRegistration ForEventAdapter(string journalPluginId, EventAdapterDetails adapter)
            => new(null, journalPluginId, adapter);

        /// <summary>The plugin, when this entry registers one.</summary>
        public PersistencePluginDetails? Plugin { get; }

        /// <summary>The journal the adapter belongs to, when this entry adds an adapter.</summary>
        public string? JournalPluginId { get; }

        /// <summary>The adapter, when this entry adds one.</summary>
        public EventAdapterDetails? EventAdapter { get; }
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// A persistence plugin registered in code: its plugin id and the config that sits under its HOCON section.
    /// </summary>
    internal abstract class PersistencePluginDetails
    {
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
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// A journal registered in code.
    /// </summary>
    internal sealed class JournalDetails : PersistencePluginDetails
    {
        private readonly Func<Config, Props> _createProps;

        private JournalDetails(string pluginId, Func<Config, Props> createProps, Config? defaultConfig, ImmutableArray<EventAdapterDetails> eventAdapters)
            : base(pluginId, defaultConfig)
        {
            _createProps = createProps;
            EventAdapters = eventAdapters;
        }

        /// <summary>
        /// The event adapters this record brings with it.
        /// </summary>
        public IReadOnlyList<EventAdapterDetails> EventAdapters { get; }

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

        /// <summary>
        /// Registers a journal whose <see cref="Props"/> the caller builds, for example from an options class.
        /// </summary>
        public static JournalDetails FromProps(string pluginId, Func<Config, Props> createProps, Config? defaultConfig = null)
            => new(pluginId, createProps ?? throw new ArgumentNullException(nameof(createProps)), defaultConfig,
                ImmutableArray<EventAdapterDetails>.Empty);

        public Props CreateProps(Config config) => _createProps(config);
    }

    /// <summary>
    /// INTERNAL API
    ///
    /// A snapshot store registered in code.
    /// </summary>
    internal sealed class SnapshotStoreDetails : PersistencePluginDetails
    {
        private readonly Func<Config, Props> _createProps;

        private SnapshotStoreDetails(string pluginId, Func<Config, Props> createProps, Config? defaultConfig)
            : base(pluginId, defaultConfig)
        {
            _createProps = createProps;
        }

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

        /// <summary>
        /// Registers a snapshot store whose <see cref="Props"/> the caller builds, for example from an options class.
        /// </summary>
        public static SnapshotStoreDetails FromProps(string pluginId, Func<Config, Props> createProps, Config? defaultConfig = null)
            => new(pluginId, createProps ?? throw new ArgumentNullException(nameof(createProps)), defaultConfig);

        public Props CreateProps(Config config) => _createProps(config);
    }
}
