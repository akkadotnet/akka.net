using LinqToDB.Data;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    public class BaseByteArrayJournalDaoConfig : IDaoConfig
    {
        public BaseByteArrayJournalDaoConfig(Configuration.Config config)
        {
            
            BufferSize = config.GetInt("buffer-size", 5000);
            BatchSize = config.GetInt("batch-size", 100);
            ReplayBatchSize = config.GetInt("replay-batch-size", 1000);
            Parallelism = config.GetInt("parallelism", 2);
            LogicalDelete = config.GetBoolean("logical-delete", false);
            MaxRowByRowSize = config.GetInt("max-row-by-row-size", 100);
            SqlCommonCompatibilityMode =
                config.GetBoolean("delete-compatibility-mode", true);
        }

        /// <summary>
        /// Specifies the batch size at which point <see cref="BulkCopyType"/>
        /// will switch to 'Default' instead of 'MultipleRows'. For smaller sets
        /// (i.e. 100 entries or less) the cost of Bulk copy setup for DB may be worse.
        /// </summary>
        public int MaxRowByRowSize { get; set; }

        public int Parallelism { get; protected set; }

        public int BatchSize { get; protected set; }

        public bool LogicalDelete { get; protected set; }

        public int ReplayBatchSize { get; protected set; }

        public int BufferSize { get; protected set; }
        
        public bool SqlCommonCompatibilityMode { get; protected set; }
        
    }
}