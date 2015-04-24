using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.SqlServer
{
    /// <summary>
    /// Configuration settings representation targeting Sql Server journal actor.
    /// </summary>
    public class JournalSettings
    {
        public const string ConfigPath = "akka.persistence.journal.sql-server";

        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Connection timeout for SQL Server related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; private set; }

        /// <summary>
        /// Schema name, where table corresponding to event journal is placed.
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// Name of the table corresponding to event journal.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// Flag determining in in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public JournalSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException("config", "SqlServer journal settings cannot be initialized, because required HOCON section couldn't been found");

            ConnectionString = config.GetString("connection-string");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name");
            TableName = config.GetString("table-name");
            AutoInitialize = config.GetBoolean("auto-initialize");
        }
    }

    /// <summary>
    /// Configuration settings representation targeting Sql Server snapshot store actor.
    /// </summary>
    public class SnapshotStoreSettings
    {
        public const string ConfigPath = "akka.persistence.snapshot-store.sql-server";

        /// <summary>
        /// Connection string used to access a persistent SQL Server instance.
        /// </summary>
        public string ConnectionString { get; private set; }

        /// <summary>
        /// Connection timeout for SQL Server related operations.
        /// </summary>
        public TimeSpan ConnectionTimeout { get; private set; }

        /// <summary>
        /// Schema name, where table corresponding to snapshot store is placed.
        /// </summary>
        public string SchemaName { get; private set; }

        /// <summary>
        /// Name of the table corresponding to snapshot store.
        /// </summary>
        public string TableName { get; private set; }

        /// <summary>
        /// Flag determining in in case of snapshot store table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        public SnapshotStoreSettings(Config config)
        {
            if (config == null) throw new ArgumentNullException("config", "SqlServer snapshot store settings cannot be initialized, because required HOCON section couldn't been found");

            ConnectionString = config.GetString("connection-string");
            ConnectionTimeout = config.GetTimeSpan("connection-timeout");
            SchemaName = config.GetString("schema-name");
            TableName = config.GetString("table-name");
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
        public readonly JournalSettings JournalSettings;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly SnapshotStoreSettings SnapshotStoreSettings;

        public SqlServerPersistenceExtension(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(SqlServerPersistence.DefaultConfiguration());

            JournalSettings = new JournalSettings(system.Settings.Config.GetConfig(JournalSettings.ConfigPath));
            SnapshotStoreSettings = new SnapshotStoreSettings(system.Settings.Config.GetConfig(SnapshotStoreSettings.ConfigPath));

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