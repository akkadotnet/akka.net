//-----------------------------------------------------------------------
// <copyright file="Extension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
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

        /// <summary>
        /// Flag determining in in case of snapshot store table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public SqliteSnapshotSettings(Config config) : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
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
        public readonly SqliteJournalSettings JournalSettings;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly SqliteSnapshotSettings SnapshotSettings;

        public SqlitePersistence(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfiguration());

            JournalSettings = new SqliteJournalSettings(system.Settings.Config.GetConfig(SqliteJournalSettings.ConfigPath));
            SnapshotSettings = new SqliteSnapshotSettings(system.Settings.Config.GetConfig(SqliteSnapshotSettings.ConfigPath));

            if (!string.IsNullOrEmpty(JournalSettings.ConnectionString))
            {
                ConnectionContext.Remember(JournalSettings.ConnectionString);
                system.TerminationTask.ContinueWith(t => ConnectionContext.Forget(JournalSettings.ConnectionString));

                if (JournalSettings.AutoInitialize)
                    DbHelper.CreateJournalTable(JournalSettings.ConnectionString, JournalSettings.TableName);
            }

            if (!string.IsNullOrEmpty(SnapshotSettings.ConnectionString))
            {
                ConnectionContext.Remember(SnapshotSettings.ConnectionString);
                system.TerminationTask.ContinueWith(t => ConnectionContext.Forget(SnapshotSettings.ConnectionString));

                if (SnapshotSettings.AutoInitialize)
                {
                    DbHelper.CreateSnapshotStoreTable(SnapshotSettings.ConnectionString, SnapshotSettings.TableName);
                }
            }
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
