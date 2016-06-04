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
    public class SqliteJournalSettings : JournalSettings
    {
        public const string ConfigPath = "akka.persistence.journal.sqlite";

        /// <summary>
        /// Flag determining in in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public SqliteJournalSettings(Config config) : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    public class SqliteSnapshotSettings : SnapshotStoreSettings
    {
        public const string ConfigPath = "akka.persistence.snapshot-store.sqlite";
        
        public SqliteSnapshotSettings(Config config) : base(config)
        {
        }
    }

    public class SqlitePersistence : IExtension
    {
        /// <summary>
        /// Returns a default configuration for akka persistence SQLite-based journals and snapshot stores.
        /// </summary>
        /// <returns></returns>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<SqlitePersistence>("Akka.Persistence.Sqlite.sqlite.conf");
        }

        public static SqlitePersistence Get(ActorSystem system)
        {
            return system.WithExtension<SqlitePersistence, SqlitePersistenceProvder>();
        }

        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config DefaultJournalConfig;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config DefaultSnapshotConfig;

        public SqlitePersistence(ExtendedActorSystem system)
        {
            var defaultConfig = DefaultConfiguration();
            system.Settings.InjectTopLevelFallback(defaultConfig);

            DefaultJournalConfig = defaultConfig.GetConfig(SqliteJournalSettings.ConfigPath);
            DefaultSnapshotConfig = defaultConfig.GetConfig(SqliteSnapshotSettings.ConfigPath);
        }
    }

    public class SqlitePersistenceProvder : ExtensionIdProvider<SqlitePersistence>
    {
        public override SqlitePersistence CreateExtension(ExtendedActorSystem system)
        {
            return new SqlitePersistence(system);
        }
    }
}
