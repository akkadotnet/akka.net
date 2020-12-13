using System;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Snapshot;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Akka.Persistence.TCK.Snapshot;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    [Collection("SqlServerSpec")]
    public class SQLServerSnapshotSpec : SnapshotStoreSpec
    {
        public static Configuration.Config Initialize(SqlServerFixture fixture)
        {
            DockerDbUtils.Initialize(fixture.ConnectionString);
            return conf;
        }
        private static  Configuration.Config conf => SQLServerSnapshotSpecConfig.Create(DockerDbUtils.ConnectionString,"snapshotSpec");

        public SQLServerSnapshotSpec(ITestOutputHelper outputHelper, SqlServerFixture fixture) :
            base(Initialize(fixture))
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