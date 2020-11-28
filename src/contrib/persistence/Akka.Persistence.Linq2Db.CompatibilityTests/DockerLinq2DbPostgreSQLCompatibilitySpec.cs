using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests;
using Akka.Persistence.Sql.Linq2Db.Tests.Docker;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.CompatibilityTests
{
    [Collection("PostgreSQLSpec")]
    public class DockerLinq2DbPostgreSQLCompatibilitySpec : CompatibilitySpec
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
                        table-compatibility-mode = ""postgres""
                        tables.journal {{ 
                           auto-init = true
                           table-name = ""{2}"" 
                           schema-name = ""public""
                           metadata-table-name = ""{3}""
                           }}
                    }}
                    postgresql {{
                                class = ""Akka.Persistence.PostgreSql.Journal.PostgreSqlJournal, Akka.Persistence.PostgreSql""
                                #plugin-dispatcher = ""akka.actor.default-dispatcher""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = ""{2}""
                                metadata-table-name = ""{3}""
                                schema-name = public
                                auto-initialize = on
                                connection-string = ""{1}""
                            }}
                }}
            }}
        ";
        
        public static Configuration.Config Create(string connString)
        {
            return ConfigurationFactory.ParseString(
                string.Format(_journalBaseConfig,
                    typeof(Linq2DbWriteJournal).AssemblyQualifiedName,
                    connString,"event_journal","metadata"));
        }

        protected override Configuration.Config Config { get; }

        protected override string OldJournal =>
            "akka.persistence.journal.postgresql";

        protected override string NewJournal =>
            "akka.persistence.journal.testspec";


        public DockerLinq2DbPostgreSQLCompatibilitySpec(ITestOutputHelper output,
            PostgreSQLFixture fixture) : base( output)
        {
            DebuggingHelpers.SetupTraceDump(output);
            Config = InitConfig(fixture);
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
            
        public static Configuration.Config InitConfig(PostgreSQLFixture fixture)
        {
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(fixture.ConnectionString);
            

            return Create(fixture.ConnectionString);
        }  
        protected void Dispose(bool disposing)
        {
            //base.Dispose(disposing);
//            DbUtils.Clean();
        }

    }
}