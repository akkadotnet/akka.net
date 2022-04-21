// //-----------------------------------------------------------------------
// // <copyright file="SqlitePersistence.cs" company="Akka.NET Project">
// //     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
// //     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// // </copyright>
// //-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Custom
{
    public class SqlitePersistence : IExtension
    {
        public const string JournalConfigPath = "akka.persistence.journal.custom-sqlite";
        public const string SnapshotConfigPath = "akka.persistence.snapshot-store.custom-sqlite";

        /// <summary>
        /// Returns a default configuration for akka persistence SQLite-based journals and snapshot stores.
        /// </summary>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<SqlitePersistence>("Akka.Persistence.Custom.sqlite.conf");
        }

        // <GetInstance>
        public static SqlitePersistence Get(ActorSystem system)
        {
            return system.WithExtension<SqlitePersistence, SqlitePersistenceProvider>();
        }
        // </GetInstance>

        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config JournalConfig;
         
        /// <summary>
        /// Journal-related default settings loaded from reference HOCON configuration.
        /// </summary>
        public readonly Config DefaultJournalConfig;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly Config SnapshotConfig;
         
        /// <summary>
        /// Snapshot store related default settings loaded from reference HOCON configuration.
        /// </summary>
        public readonly Config DefaultSnapshotConfig;

        //<Constructor>
        public SqlitePersistence(ExtendedActorSystem system)
        {
            var defaultConfig = DefaultConfiguration();
            system.Settings.InjectTopLevelFallback(defaultConfig);

            JournalConfig = system.Settings.Config.GetConfig(JournalConfigPath);
            DefaultJournalConfig = defaultConfig.GetConfig(JournalConfigPath);
            SnapshotConfig = system.Settings.Config.GetConfig(SnapshotConfigPath);
            DefaultSnapshotConfig = defaultConfig.GetConfig(SnapshotConfigPath);
        }
        //</Constructor>
    }

    //<ExtensionIdProvider>
    public class SqlitePersistenceProvider : ExtensionIdProvider<SqlitePersistence>
    {
        public override SqlitePersistence CreateExtension(ExtendedActorSystem system)
        {
            return new SqlitePersistence(system);
        }
    }
    //</ExtensionIdProvider>
}