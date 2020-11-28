using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Tests;
using JetBrains.dotMemoryUnit;
using LinqToDB;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db
{
    /*
    public class PostgreSqlLinq2DbJournalPerfSpec : L2dbJournalPerfSpec
    {
        
        private static readonly  Config conf = PostgreSQLJournalSpecConfig.Create(PostgreSQLJournalSpec.connString,ProviderName.PostgreSQL95);
        public PostgreSqlLinq2DbJournalPerfSpec(ITestOutputHelper output)
            : base(conf, "PostgreSql", output,eventsCount: TestConstants.NumMessages)
        {
            //LinqToDB.Common.Configuration.ContinueOnCapturedContext = false;
            DotMemoryUnitTestOutput.SetOutputMethod(
                message => output.WriteLine(message));
            var connFactory = new AkkaPersistenceDataConnectionFactory(new JournalConfig(conf.GetConfig("akka.persistence.journal.testspec")));
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
        
    }*/
}