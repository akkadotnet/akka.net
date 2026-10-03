//-----------------------------------------------------------------------
// <copyright file="SqlitePersistenceSetupExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using Akka.Persistence.Embedded.Journal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Embedded.Snapshot;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;

namespace Akka.Persistence.Embedded
{
    /// <summary>
    /// Registers the plugin in a <see cref="PersistenceSetup"/>, so Akka.Persistence builds it without reflection. A
    /// registered plugin needs no <c>class</c> setting in HOCON and brings its reference configuration with it, so the
    /// only HOCON a user writes is the plugin choice and the <c>connection-string</c>. Required when the
    /// <c>Akka.DynamicTypeLoading</c> feature switch is off (Native AOT, trimmed apps); optional otherwise.
    /// </summary>
    public static class SqlitePersistenceSetupExtensions
    {
        /// <summary>
        /// Registers the journal, the snapshot store and the read journal under their default plugin ids
        /// (<see cref="SqlitePersistence.JournalPluginId"/>, <see cref="SqlitePersistence.SnapshotStorePluginId"/>,
        /// <see cref="SqlitePersistence.QueryPluginId"/>), each with its reference configuration. Event adapters for the
        /// journal go in <paramref name="eventAdapters"/>.
        /// </summary>
        public static PersistenceSetup WithEmbeddedPersistence(this PersistenceSetup setup, IEnumerable<EventAdapterDetails>? eventAdapters = null)
            => setup
                .WithEmbeddedJournal(eventAdapters: eventAdapters)
                .WithEmbeddedSnapshotStore()
                .WithEmbeddedReadJournal();

        /// <summary>Registers <see cref="SqliteWriteJournal"/> under <paramref name="pluginId"/>, with its event adapters.</summary>
        public static PersistenceSetup WithEmbeddedJournal(
            this PersistenceSetup setup,
            string pluginId = SqlitePersistence.JournalPluginId,
            IEnumerable<EventAdapterDetails>? eventAdapters = null)
            => setup.WithJournal(pluginId, static config => new SqliteWriteJournal(config), SqlitePersistence.DefaultJournalConfiguration, eventAdapters);

        /// <summary>Registers <see cref="SqliteSnapshotStore"/> under <paramref name="pluginId"/>.</summary>
        public static PersistenceSetup WithEmbeddedSnapshotStore(this PersistenceSetup setup, string pluginId = SqlitePersistence.SnapshotStorePluginId)
            => setup.WithSnapshotStore(pluginId, static config => new SqliteSnapshotStore(config), SqlitePersistence.DefaultSnapshotConfiguration);

        /// <summary>
        /// Registers the read journal under <paramref name="pluginId"/>. Its <c>write-plugin</c> setting says which journal
        /// it reads (the default journal id unless the HOCON section says otherwise).
        /// </summary>
        public static PersistenceSetup WithEmbeddedReadJournal(this PersistenceSetup setup, string pluginId = SqlitePersistence.QueryPluginId)
        {
            if (string.IsNullOrWhiteSpace(pluginId))
                throw new ArgumentException("A plugin id is required.", nameof(pluginId));

            return setup.WithReadJournal(
                pluginId,
                (system, config) => new SqliteReadJournalProvider(system, config, pluginId),
                SqlitePersistence.DefaultQueryConfiguration);
        }
    }
}
