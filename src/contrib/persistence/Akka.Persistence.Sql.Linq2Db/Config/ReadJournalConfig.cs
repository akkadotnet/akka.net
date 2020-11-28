using System;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class ReadJournalConfig : IProviderConfig<JournalTableConfig>
    {
        public ReadJournalConfig(Configuration.Config config)
        {
            ConnectionString = config.GetString("connection-string");
            ProviderName = config.GetString("provider-name");
            TableConfig = new JournalTableConfig(config);
            DaoConfig = new BaseByteArrayJournalDaoConfig(config);
            var dbConf = config.GetString(ConfigKeys.useSharedDb);
            UseCloneConnection =
                config.GetBoolean("use-clone-connection", false);
            JournalSequenceRetrievalConfiguration = new JournalSequenceRetrievalConfig(config);
            PluginConfig = new ReadJournalPluginConfig(config);
            RefreshInterval = config.GetTimeSpan("refresh-interval",
                TimeSpan.FromSeconds(1));
            MaxBufferSize = config.GetInt("max-buffer-size", 500);
            AddShutdownHook = config.GetBoolean("add-shutdown-hook", true);
            IncludeDeleted =
                config.GetBoolean("include-logically-deleted", true);
        }

        public BaseByteArrayJournalDaoConfig DaoConfig { get; set; }

        public int MaxBufferSize { get; set; }

        public bool AddShutdownHook { get; set; }

        public ReadJournalPluginConfig PluginConfig { get; set; }

        public TimeSpan RefreshInterval { get; set; }

        public JournalSequenceRetrievalConfig JournalSequenceRetrievalConfiguration { get; set; }

        public bool IncludeDeleted { get; set; }

        public string ProviderName { get; }
        public string ConnectionString { get; }
        public JournalTableConfig TableConfig { get; }
        public IDaoConfig IDaoConfig
        {
            get { return DaoConfig; }
        }
        public bool UseCloneConnection { get; }
        public string DefaultSerializer { get; set; }
    }
}