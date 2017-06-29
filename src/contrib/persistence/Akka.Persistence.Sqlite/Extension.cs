//-----------------------------------------------------------------------
// <copyright file="Extension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.Common;

namespace Akka.Persistence.Sqlite
{
    /// <summary>
    /// TBD
    /// </summary>
    public class SqliteJournalSettings : JournalSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        public const string ConfigPath = "akka.persistence.journal.sqlite";

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public SqliteJournalSettings(Config config) : base(config)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SqliteSnapshotSettings : SnapshotStoreSettings
    {
        /// <summary>
        /// TBD
        /// </summary>
        public const string ConfigPath = "akka.persistence.snapshot-store.sqlite";

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="config">TBD</param>
        public SqliteSnapshotSettings(Config config) : base(config)
        {
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SqlitePersistence : IExtension
    {
        /// <summary>
        /// Returns a default configuration for akka persistence SQLite-based journals and snapshot stores.
        /// </summary>
        /// <returns>TBD</returns>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<SqlitePersistence>("Akka.Persistence.Sqlite.sqlite.conf");
        }

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public static SqlitePersistence Get(ActorSystem system)
        {
            return system.WithExtension<SqlitePersistence, SqlitePersistenceProvider>();
        }

        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config DefaultJournalConfig;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config DefaultSnapshotConfig;

        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        public SqlitePersistence(ExtendedActorSystem system)
        {
            var defaultConfig = DefaultConfiguration();
            system.Settings.InjectTopLevelFallback(defaultConfig);

            DefaultJournalConfig = defaultConfig.GetConfig(SqliteJournalSettings.ConfigPath);
            DefaultSnapshotConfig = defaultConfig.GetConfig(SqliteSnapshotSettings.ConfigPath);
        }
    }

    /// <summary>
    /// TBD
    /// </summary>
    public class SqlitePersistenceProvider : ExtensionIdProvider<SqlitePersistence>
    {
        /// <summary>
        /// TBD
        /// </summary>
        /// <param name="system">TBD</param>
        /// <returns>TBD</returns>
        public override SqlitePersistence CreateExtension(ExtendedActorSystem system)
        {
            return new SqlitePersistence(system);
        }
    }
}