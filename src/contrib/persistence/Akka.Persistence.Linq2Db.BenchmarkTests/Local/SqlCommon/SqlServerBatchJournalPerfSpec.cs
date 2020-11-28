using System;
using Akka.Configuration;
using Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests;
using JetBrains.dotMemoryUnit;
using LinqToDB;
using LinqToDB.Data;
using Xunit.Abstractions;
using Config = Akka.Configuration.Config;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.SqlCommon
{
    public class SqlServerBatchJournalPerfSpec : L2dbJournalPerfSpec
    {
        public SqlServerBatchJournalPerfSpec(ITestOutputHelper output) : base(InitConfig(),"sqlserverperfspec", output, eventsCount: TestConstants.NumMessages)
        {
            DotMemoryUnitTestOutput.SetOutputMethod(
                message => output.WriteLine(message));
            using (var conn =
                new DataConnection(ProviderName.SqlServer2008, ConnectionString.Instance.Replace("\\\\","\\")))
            {
                try
                {
                    conn.GetTable<JournalRow>().TableName("EventJournal_batch").Delete();
                }
                catch (Exception e)
                {
                }
                
                //Akka.Persistence.SqlServer.Journal.BatchingSqlServerJournal
            }
        }
        
        public static Config InitConfig()
        {
            DbUtils.ConnectionString = ConnectionString.Instance;
            //need to make sure db is created before the tests start
            //DbUtils.Initialize(connString);
            var specString = $@"
                    akka.persistence {{
                        publish-plugin-commands = on
                        journal {{
                            plugin = ""akka.persistence.journal.sql-server""
                            sql-server {{
                                class = ""Akka.Persistence.SqlServer.Journal.BatchingSqlServerJournal, Akka.Persistence.SqlServer""
                                plugin-dispatcher = ""akka.persistence.dispatchers.default-plugin-dispatcher""
                                table-name = EventJournal_batch
                                schema-name = dbo
                                auto-initialize = on
                                connection-string = ""{DbUtils.ConnectionString}""
                            }}
                        }}
                    }}";

            return ConfigurationFactory.ParseString(specString);
        }
    }
}