//-----------------------------------------------------------------------
// <copyright file="SqlitePersistenceSetupExtensions.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Persistence.Embedded.Journal;
using Akka.Persistence.Embedded.Query;
using Akka.Persistence.Embedded.Snapshot;
using Akka.Persistence.Query;

namespace Akka.Persistence.Embedded
{
    /// <summary>
    /// Registers the plugin's types in code, so Akka.Persistence can build them without reflection.
    /// Required when the <c>Akka.DynamicTypeLoading</c> feature switch is off (Native AOT, trimmed apps).
    /// </summary>
    public static class SqlitePersistenceSetupExtensions
    {
        /// <summary>Registers <see cref="SqliteWriteJournal"/> and <see cref="SqliteSnapshotStore"/>.</summary>
        public static PersistencePluginSetup WithEmbeddedPersistence(this PersistencePluginSetup setup)
            => setup
                .WithJournal(static config => new SqliteWriteJournal(config))
                .WithSnapshotStore(static config => new SqliteSnapshotStore(config));

        /// <summary>Registers <see cref="SqliteReadJournalProvider"/> for <see cref="PersistenceQuery"/>.</summary>
        public static PersistenceQuerySetup WithEmbeddedReadJournal(this PersistenceQuerySetup setup)
            => setup.WithReadJournal(static (system, config) => new SqliteReadJournalProvider(system, config));
    }
}
