using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Sql.Common;

namespace Akka.Persistence.PostgreSql
{
    /// <summary>
    /// Configuration settings representation targeting PostgreSql journal actor.
    /// </summary>
    public class PostgreSqlJournalSettings : JournalSettings
    {
        public const string JournalConfigPath = "akka.persistence.journal.postgresql";

        /// <summary>
        /// Flag determining in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public PostgreSqlJournalSettings(Config config)
            : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    /// <summary>
    /// Configuration settings representation targeting PostgreSql snapshot store actor.
    /// </summary>
    public class PostgreSqlSnapshotStoreSettings : SnapshotStoreSettings
    {
        public const string SnapshotStoreConfigPath = "akka.persistence.snapshot-store.postgresql";

        /// <summary>
        /// Flag determining in case of snapshot store table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public PostgreSqlSnapshotStoreSettings(Config config)
            : base(config)
        {
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    /// <summary>
    /// An actor system extension initializing support for PostgreSql persistence layer.
    /// </summary>
    public class PostgreSqlPersistenceExtension : IExtension
    {
        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly PostgreSqlJournalSettings JournalSettings;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly PostgreSqlSnapshotStoreSettings SnapshotStoreSettings;

        public PostgreSqlPersistenceExtension(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(PostgreSqlPersistence.DefaultConfiguration());

            JournalSettings = new PostgreSqlJournalSettings(system.Settings.Config.GetConfig(PostgreSqlJournalSettings.JournalConfigPath));
            SnapshotStoreSettings = new PostgreSqlSnapshotStoreSettings(system.Settings.Config.GetConfig(PostgreSqlSnapshotStoreSettings.SnapshotStoreConfigPath));

            if (JournalSettings.AutoInitialize)
            {
                PostgreSqlInitializer.CreatePostgreSqlJournalTables(JournalSettings.ConnectionString, JournalSettings.SchemaName, JournalSettings.TableName);
            }

            if (SnapshotStoreSettings.AutoInitialize)
            {
                PostgreSqlInitializer.CreatePostgreSqlSnapshotStoreTables(SnapshotStoreSettings.ConnectionString, SnapshotStoreSettings.SchemaName, SnapshotStoreSettings.TableName);
            }
        }
    }

    /// <summary>
    /// Singleton class used to setup PostgreSQL backend for akka persistence plugin.
    /// </summary>
    public class PostgreSqlPersistence : ExtensionIdProvider<PostgreSqlPersistenceExtension>
    {
        public static readonly PostgreSqlPersistence Instance = new PostgreSqlPersistence();

        /// <summary>
        /// Initializes a PostgreSQL persistence plugin inside provided <paramref name="actorSystem"/>.
        /// </summary>
        public static void Init(ActorSystem actorSystem)
        {
            Instance.Apply(actorSystem);
        }

        private PostgreSqlPersistence() { }
        
        /// <summary>
        /// Creates an actor system extension for akka persistence PostgreSQL support.
        /// </summary>
        /// <param name="system"></param>
        /// <returns></returns>
        public override PostgreSqlPersistenceExtension CreateExtension(ExtendedActorSystem system)
        {
            return new PostgreSqlPersistenceExtension(system);
        }

        /// <summary>
        /// Returns a default configuration for akka persistence PostgreSQL-based journals and snapshot stores.
        /// </summary>
        /// <returns></returns>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<PostgreSqlPersistence>("Akka.Persistence.PostgreSql.postgresql.conf");
        }
    }
}