using System;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using Akka.Persistence.TCK.Snapshot;
using LinqToDB;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    public class SQLServerSnapshotSpec : SnapshotStoreSpec
    {
        private static readonly  Configuration.Config conf = SQLServerSnapshotSpecConfig.Create(ConnectionString.Instance,"snapshotSpec");

        public SQLServerSnapshotSpec(ITestOutputHelper outputHelper) :
            base(conf)
        {
            DebuggingHelpers.SetupTraceDump(outputHelper);
            var connFactory = new AkkaPersistenceDataConnectionFactory(
                new SnapshotConfig(
                    conf.GetConfig("akka.persistence.snapshot-store.linq2db")));
            using (var conn = connFactory.GetConnection())
            {
                
                try
                {
                    conn.GetTable<SnapshotRow>().Delete();
                }
                catch (Exception e)
                {

                }
            }
            
            Initialize();
        }
    }
}