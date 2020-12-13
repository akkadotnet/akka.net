using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [Collection("SqlServerSpec")]
    public class SqlServerSqlCommonSnapshotCompatibilitySpec : SqlCommonSnapshotCompatibilitySpec
    {
        
        public SqlServerSqlCommonSnapshotCompatibilitySpec(ITestOutputHelper outputHelper, SqlServerFixture fixture) : base(outputHelper)
        {
            DockerDbUtils.Initialize(fixture.ConnectionString);
        }

        protected override string OldSnapshot =>
            "akka.persistence.snapshot-store.sql-server";

        protected override string NewSnapshot =>
            "akka.persistence.snapshot-store.linq2db";

        protected override Configuration.Config Config =>
            SqlServerCompatibilitySpecConfig.InitSnapshotConfig("snapshot_compat");
    }
}