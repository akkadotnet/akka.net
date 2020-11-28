using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Snapshot;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class SnapshotDaoConfig : IDaoConfig
    {
        public SnapshotDaoConfig(bool sqlCommonCompatibilityMode)
        {
            SqlCommonCompatibilityMode = sqlCommonCompatibilityMode;
        }
        public bool SqlCommonCompatibilityMode { get; }
        public int Parallelism { get; }
    }
    public class SnapshotConfig : IProviderConfig<SnapshotTableConfiguration>
    {
        public SnapshotConfig(Configuration.Config config)
        {
            config =
                config.SafeWithFallback(Linq2DbSnapshotStore
                    .DefaultConfiguration.GetConfig("akka.persistence.snapshot-store.linq2db"));
            TableConfig = new SnapshotTableConfiguration(config);
            PluginConfig = new SnapshotPluginConfig(config);
            var dbConf = config.GetString(ConfigKeys.useSharedDb);
            UseSharedDb = string.IsNullOrWhiteSpace(dbConf) ? null : dbConf;
            DefaultSerializer = config.GetString("serializer", null);
            ConnectionString = config.GetString("connection-string", null);
            ProviderName = config.GetString("provider-name", null);
            IDaoConfig =
                new SnapshotDaoConfig(config.GetBoolean("compatibility-mode",
                    false));
        }

        public string ProviderName { get; }
        public string ConnectionString { get; }
        public SnapshotTableConfiguration TableConfig { get; }
        public IDaoConfig IDaoConfig { get; }
        public bool UseCloneConnection { get; }
        public string DefaultSerializer { get; protected set; }

        public string UseSharedDb { get; protected set; }

        public SnapshotPluginConfig PluginConfig { get; protected set; }
        
    }
}