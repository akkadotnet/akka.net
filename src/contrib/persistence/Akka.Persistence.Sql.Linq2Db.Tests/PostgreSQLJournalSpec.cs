using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using Akka.Persistence.TCK.Journal;
using Akka.Util.Internal;
using LinqToDB;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Sql.Linq2Db.Tests
{
    
    [Collection("PostgreSQLSpec")]
    public class DockerLinq2DbPostgreSQLJournalSpec : JournalSpec
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
        
        public static Configuration.Config Create(string connString)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbWriteJournal).AssemblyQualifiedName,
                    connString,"testJournal"));
        }
        public DockerLinq2DbPostgreSQLJournalSpec(ITestOutputHelper output,
            PostgreSQLFixture fixture) : base(InitConfig(fixture),
            "postgresperf", output)
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

            Initialize();
        }
            
        public static Configuration.Config InitConfig(PostgreSQLFixture fixture)
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

        protected override bool SupportsSerialization => false;
    
    }
    
}