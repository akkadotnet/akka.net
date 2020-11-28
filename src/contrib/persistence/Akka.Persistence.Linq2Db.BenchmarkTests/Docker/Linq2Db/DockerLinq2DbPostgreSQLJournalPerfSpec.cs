using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Docker.Linq2Db
{
    [Collection("PostgreSQLSpec")]
    public class DockerLinq2DbPostgreSQLJournalPerfSpec : L2dbJournalPerfSpec
    {
        public static string _journalBaseConfig = @"
            akka.persistence {{
                publish-plugin-commands = on
                journal {{
                    plugin = ""akka.persistence.journal.testspec""
                    testspec {{
                        class = ""{0}""
                        #plugin-dispatcher = ""akka.actor.default-dispatcher""
                        plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                
                        connection-string = ""{1}""
#connection-string = ""FullUri=file:test.db&cache=shared""
                        provider-name = """ + LinqToDB.ProviderName.PostgreSQL95 + @"""
                        use-clone-connection = true
                        tables.journal {{ 
                           auto-init = true
                           table-name = ""{2}"" 
                           }}
                    }}
                }}
            }}
        ";
        
        public static Config Create(string connString)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbWriteJournal).AssemblyQualifiedName,
                    connString,"testPerfTable"));
        }
        public DockerLinq2DbPostgreSQLJournalPerfSpec(ITestOutputHelper output,
            PostgreSQLFixture fixture) : base(InitConfig(fixture),
            "postgresperf", output,40, eventsCount: TestConstants.DockerNumMessages)
        {
            
            var connFactory = new AkkaPersistenceDataConnectionFactory(new JournalConfig(Create(DockerDbUtils.ConnectionString).GetConfig("akka.persistence.journal.testspec")));
            using (var conn = connFactory.GetConnection())
            {
                try
                {
                    conn.GetTable<JournalRow>().Delete();
                }
                catch (Exception e)
                {
                }
                
            }
        }
            
        public static Config InitConfig(PostgreSQLFixture fixture)
        {
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(fixture.ConnectionString);
            

            return Create(fixture.ConnectionString);
        }  
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
//            DbUtils.Clean();
        }

        [Fact]
        public void PersistenceActor_Must_measure_PersistGroup1000()
        {
            RunGroupBenchmark(1000,10);
        }
    }
}