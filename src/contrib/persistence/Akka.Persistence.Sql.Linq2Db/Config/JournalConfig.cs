using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class JournalConfig : IProviderConfig<JournalTableConfig>
    {
        public JournalConfig(Configuration.Config config)
        {
            config =
                config.SafeWithFallback(
                    Linq2DbWriteJournal.DefaultConfiguration);
            MaterializerDispatcher = config.GetString("materializer-dispatcher","akka.actor.default-dispatcher");
            ConnectionString = config.GetString("connection-string");
            ProviderName = config.GetString("provider-name");
            TableConfig = new JournalTableConfig(config);
            PluginConfig = new JournalPluginConfig(config);
            DaoConfig = new BaseByteArrayJournalDaoConfig(config);
            var dbConf = config.GetString(ConfigKeys.useSharedDb);
            UseSharedDb = string.IsNullOrWhiteSpace(dbConf) ? null : dbConf;
            UseCloneConnection =
                config.GetBoolean("use-clone-connection", false);
            
        }
        
        public string MaterializerDispatcher { get; protected set; }
        
        public string UseSharedDb { get; protected set; }

        public BaseByteArrayJournalDaoConfig DaoConfig { get; protected set; }
        public IDaoConfig IDaoConfig
        {
            get { return DaoConfig; }
        }

        public JournalPluginConfig PluginConfig { get; protected set; }

        public JournalTableConfig TableConfig { get;
            protected set;
        }

        public string DefaultSerializer { get; set; }
        public string ProviderName { get; }
        public string ConnectionString { get; }
        public bool UseCloneConnection { get; set; }
    }

    public interface IProviderConfig<TTable>
    {
        string ProviderName { get; }
        string ConnectionString { get; }
        TTable TableConfig { get; }
        IDaoConfig IDaoConfig { get; }
        bool UseCloneConnection { get; }
        string DefaultSerializer { get; }
    }

    public interface IDaoConfig
    {
        bool SqlCommonCompatibilityMode { get; }
        int Parallelism { get; }
    }
}