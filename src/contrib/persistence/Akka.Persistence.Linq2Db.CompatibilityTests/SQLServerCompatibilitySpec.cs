using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [Collection("SqlServerSpec")]
    public class SQLServerCompatibilitySpec : CompatibilitySpec
    {
        
        public SQLServerCompatibilitySpec(ITestOutputHelper outputHelper, SqlServerFixture fixture) : base(outputHelper)
        {
            DockerDbUtils.Initialize(fixture.ConnectionString);
        }

        protected override string OldJournal =>
            "akka.persistence.journal.sql-server";

        protected override string NewJournal =>
            "akka.persistence.journal.testspec";

        protected override Configuration.Config Config =>
            SqlServerCompatibilitySpecConfig.InitConfig("journal_compat",
                "journal_metadata_compat");
    }
}