using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Snapshot;

namespace Akka.Persistence.Sql.Linq2Db.Config
{
    
    public class SnapshotTableConfiguration
    {
        public SnapshotTableConfiguration(Configuration.Config config)
        {
            config =
                config.SafeWithFallback(Linq2DbSnapshotStore
                    .DefaultConfiguration);
            var localcfg = config.GetConfig("tables.snapshot");
            ColumnNames= new SnapshotTableColumnNames(config);
            TableName = localcfg.GetString("table-name", "snapshot");
            SchemaName = localcfg.GetString("schema-name", null);
            AutoInitialize = localcfg.GetBoolean("auto-init", false);
        }
        public SnapshotTableColumnNames ColumnNames { get; protected set; }
        public string TableName { get; protected set; }
        public string SchemaName { get; protected set; }
        public bool AutoInitialize { get; protected set; }
        public override int GetHashCode()
        {
            return HashCode.Combine(ColumnNames, TableName, SchemaName);
        }
    }
}