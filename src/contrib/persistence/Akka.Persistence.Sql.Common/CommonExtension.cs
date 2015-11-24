//-----------------------------------------------------------------------
// <copyright file="Extension.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Sql.Common
{
    public class CommonJournalSettings : JournalSettings
    {
        public const string ConfigPath = "akka.persistence.journal.common";

        /// <summary>
        /// Name of the db provider
        /// </summary>
        public string ProviderName { get; private set; }

        /// <summary>
        /// Flag determining in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        /// <summary>
        /// Flag determining to open and hold one connection to the datasource.
        /// </summary>
        public bool PersistentConnection { get; private set; }
        
        public CommonJournalSettings(Config config) : base(config)
        {
            ProviderName = config.GetString("provider-name");
            AutoInitialize = config.GetBoolean("auto-initialize");
            PersistentConnection = config.GetBoolean("persistent-connection");
        }
    }

    public class CommonSnapshotSettings : SnapshotStoreSettings
    {
        public const string ConfigPath = "akka.persistence.snapshot-store.common";

        /// <summary>
        /// Name of the db provider
        /// </summary>
        public string ProviderName { get; private set; }

        /// <summary>
        /// Flag determining in in case of event journal table missing, it should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        /// <summary>
        /// Flag determining to open and hold one connection to the datasource.
        /// </summary>
        public bool PersistentConnection { get; private set; }


        public CommonSnapshotSettings(Config config) : base(config)
        {
            ProviderName = config.GetString("provider-name");
            AutoInitialize = config.GetBoolean("auto-initialize");
            PersistentConnection = config.GetBoolean("persistent-connection");
        }
    }

    public class CommonPersistence : IExtension
    {
        /// <summary>
        /// Returns a default configuration for akka persistence SQLite-based journals and snapshot stores.
        /// </summary>
        /// <returns></returns>
        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<CommonPersistence>("Akka.Persistence.Sql.Common.common.conf");
        }

        public static CommonPersistence Get(ActorSystem system)
        {
            return system.WithExtension<CommonPersistence, CommonPersistenceProvder>();
        }

        /// <summary>
        /// Journal-related settings loaded from HOCON configuration.
        /// </summary>
        public readonly CommonJournalSettings JournalSettings;

        /// <summary>
        /// Snapshot store related settings loaded from HOCON configuration.
        /// </summary>
        public readonly CommonSnapshotSettings SnapshotSettings;

        public CommonPersistence(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfiguration());

            JournalSettings = new CommonJournalSettings(system.Settings.Config.GetConfig(CommonJournalSettings.ConfigPath));
            SnapshotSettings = new CommonSnapshotSettings(system.Settings.Config.GetConfig(CommonSnapshotSettings.ConfigPath));

            if (!string.IsNullOrEmpty(JournalSettings.ConnectionString))
            {         
                if(JournalSettings.PersistentConnection)
                { 
                    CommonConnectionContext.Remember(JournalSettings.ConnectionString, JournalSettings.ProviderName);
                    system.TerminationTask.ContinueWith(t => CommonConnectionContext.Forget(JournalSettings.ConnectionString));
                }

                using(var connection = CommonConnectionContext.CreateConnection(JournalSettings.ConnectionString, JournalSettings.ProviderName))
                {
                    if (JournalSettings.AutoInitialize)
                        CommonDbHelper.CreateJournalTable(connection, JournalSettings.TableName, JournalSettings.SchemaName);
                }
            }

            if (!string.IsNullOrEmpty(SnapshotSettings.ConnectionString))
            {
                if(SnapshotSettings.PersistentConnection)
                { 
                    CommonConnectionContext.Remember(SnapshotSettings.ConnectionString, JournalSettings.ProviderName);
                    system.TerminationTask.ContinueWith(t => CommonConnectionContext.Forget(SnapshotSettings.ConnectionString));
                }

                using(var connection = CommonConnectionContext.CreateConnection(SnapshotSettings.ConnectionString, JournalSettings.ProviderName))
                { 
                    if (SnapshotSettings.AutoInitialize)
                        CommonDbHelper.CreateSnapshotStoreTable(connection, SnapshotSettings.TableName, SnapshotSettings.SchemaName);
                }
            }
        }

        public void DropJournalTable(bool recreate = true)
        {
            using(var connection = CommonConnectionContext.CreateConnection(JournalSettings.ConnectionString, JournalSettings.ProviderName))
            {
                CommonDbHelper.DropTable(connection, JournalSettings.TableName, JournalSettings.SchemaName);
                if(recreate)
                    CommonDbHelper.CreateJournalTable(connection, JournalSettings.TableName, JournalSettings.SchemaName);
            }
        }

        public void DropSnapshotTable(bool recreate = true)
        {
            using(var connection = CommonConnectionContext.CreateConnection(SnapshotSettings.ConnectionString, JournalSettings.ProviderName))
            {
                CommonDbHelper.DropTable(connection, SnapshotSettings.TableName, SnapshotSettings.SchemaName);
                if (recreate)
                    CommonDbHelper.CreateSnapshotStoreTable(connection, SnapshotSettings.TableName, SnapshotSettings.SchemaName);
            }
        }
    }

    public class CommonPersistenceProvder : ExtensionIdProvider<CommonPersistence>
    {
        public override CommonPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new CommonPersistence(system);
        }
    }
}
