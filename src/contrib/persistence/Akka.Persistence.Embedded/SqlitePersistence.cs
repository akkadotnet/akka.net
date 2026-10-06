//-----------------------------------------------------------------------
// <copyright file="SqlitePersistence.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using Akka.Configuration;

namespace Akka.Persistence.Embedded
{
    /// <summary>
    /// Plugin ids and reference configuration of the SQLite persistence plugin.
    /// </summary>
    public static class SqlitePersistence
    {
        /// <summary>Config path of the journal plugin.</summary>
        public const string JournalPluginId = "akka.persistence.journal.embedded";

        /// <summary>Config path of the snapshot store plugin.</summary>
        public const string SnapshotStorePluginId = "akka.persistence.snapshot-store.embedded";

        /// <summary>Config path of the read journal plugin.</summary>
        public const string QueryPluginId = "akka.persistence.query.journal.embedded";

        /// <summary>
        /// The reference configuration of all three plugins. Add it as a fallback to your own configuration.
        /// </summary>
        public static Config DefaultConfiguration { get; } = ConfigurationFactory.ParseString(SqliteReferenceConfig.ReferenceHocon);

        /// <summary>The journal section of <see cref="DefaultConfiguration"/>.</summary>
        public static Config DefaultJournalConfiguration { get; } = DefaultConfiguration.GetConfig(JournalPluginId);

        /// <summary>The snapshot store section of <see cref="DefaultConfiguration"/>.</summary>
        public static Config DefaultSnapshotConfiguration { get; } = DefaultConfiguration.GetConfig(SnapshotStorePluginId);

        /// <summary>The read journal section of <see cref="DefaultConfiguration"/>.</summary>
        public static Config DefaultQueryConfiguration { get; } = DefaultConfiguration.GetConfig(QueryPluginId);
    }
}
