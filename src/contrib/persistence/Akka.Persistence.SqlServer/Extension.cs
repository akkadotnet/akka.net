using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.Common;

namespace Akka.Persistence.SqlServer
{

    public class SqlServerJournalSettings : JournalSettings
    {
        public const string ConfigPath = "akka.persistence.journal.sql-server";

        /// <summary>
        /// Flag determining in in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public SqlServerJournalSettings(Config config) : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    public class SqlServerSnapshotSettings : SnapshotStoreSettings
    {
        public const string ConfigPath = "akka.persistence.snapshot-store.sql-server";

        /// <summary>
        /// Flag determining in in case of snapshot store table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public SqlServerSnapshotSettings(Config config) : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    /// <summary>
    /// An actor system extension initializing support for SQL Server persistence layer.
    /// </summary>
    public class SqlServerPersistenceExtension : IExtension
    {
        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly SqlServerJournalSettings JournalSettings;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly SqlServerSnapshotSettings SnapshotStoreSettings;

        public SqlServerPersistenceExtension(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(SqlServerPersistence.DefaultConfiguration());

            JournalSettings = new SqlServerJournalSettings(system.Settings.Config.GetConfig(SqlServerJournalSettings.ConfigPath));
            SnapshotStoreSettings = new SqlServerSnapshotSettings(system.Settings.Config.GetConfig(SqlServerSnapshotSettings.ConfigPath));

            if (JournalSettings.AutoInitialize)
            {
                SqlServerInitializer.CreateSqlServerJournalTables(JournalSettings.ConnectionString, JournalSettings.SchemaName, JournalSettings.TableName);
            }

            if (SnapshotStoreSettings.AutoInitialize)
            {
                SqlServerInitializer.CreateSqlServerSnapshotStoreTables(SnapshotStoreSettings.ConnectionString, SnapshotStoreSettings.SchemaName, SnapshotStoreSettings.TableName);
            }
        }
    }

    /// <summary>
    /// Singleton class used to setup SQL Server backend for akka persistence plugin.
    /// </summary>
    public class SqlServerPersistence : ExtensionIdProvider<SqlServerPersistenceExtension>
    {
        public static readonly SqlServerPersistence Instance = new SqlServerPersistence();

        /// <summary>
        /// Initializes a SQL Server persistence plugin inside provided <paramref name="actorSystem"/>.
        /// </summary>
        public static void Init(ActorSystem actorSystem)
        {
            Instance.Apply(actorSystem);
        }

        private SqlServerPersistence() { }
        
        /// <summary>
        /// Creates an actor system extension for akka persistence SQL Server support.
        /// </summary>
        /// <param name="system"></param>
        /// <returns></returns>
        public override SqlServerPersistenceExtension CreateExtension(ExtendedActorSystem system)
        {
            return new SqlServerPersistenceExtension(system);
        }

        /// <summary>
        /// Returns a default configuration for akka persistence SQL Server-based journals and snapshot stores.
        /// </summary>
        /// <returns></returns>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<SqlServerPersistence>("Akka.Persistence.SqlServer.sql-server.conf");
        }
    }
}