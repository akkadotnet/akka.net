using Akka.Configuration;
using Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Docker.SqlCommon
{
    [Collection("SqlServerSpec")]
    public class DockerBatchingSqlServerJournalPerfSpec : L2dbJournalPerfSpec
    {
        public DockerBatchingSqlServerJournalPerfSpec(ITestOutputHelper output, SqlServerFixture fixture) : base(InitConfig(fixture),"sqlserverperfspec", output,40, TestConstants.DockerNumMessages)
        {
        }
        public static Config InitConfig(SqlServerFixture fixture)
        {
            //need to make sure db is created before the tests start
            DockerDbUtils.Initialize(fixture.ConnectionString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        journal {{
                            plugin = ""akka.persistence.journal.sql-server""
                            sql-server {{
                                class = ""Akka.Persistence.SqlServer.Journal.BatchingSqlServerJournal, Akka.Persistence.SqlServer""
                                #plugin-dispatcher = ""akka.actor.default-dispatcher""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = EventJournal
                                schema-name = dbo
                                auto-initialize = on
                                connection-string = ""{DockerDbUtils.ConnectionString}""
                            }}
                        }}
                    }}";

            return ConfigurationFactory.ParseString(specString);
        }
        [Fact]
        public void PersistenceActor_Must_measure_PersistGroup1000()
        {
            RunGroupBenchmark(1000,10);
        }
    }
}