using System;
using Akka.Configuration;
using Akka.Persistence.Sql.Linq2Db;
using Akka.Persistence.Sql.Linq2Db.Config;
using Akka.Persistence.Sql.Linq2Db.Db;
using Akka.Persistence.Sql.Linq2Db.Journal;
using Akka.Persistence.Sql.Linq2Db.Journal.Types;
using Akka.Persistence.Sql.Linq2Db.Tests;
using JetBrains.dotMemoryUnit;
using LinqToDB;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Linq2Db.BenchmarkTests.Local.Linq2Db
{
    public class SQLServerLinq2DbJournalPerfSpec : L2dbJournalPerfSpec
    {
        
        private static readonly  Config conf = SQLServerJournalSpecConfig.Create(ConnectionString.Instance,"journalPerfSpec",200,2);
        public SQLServerLinq2DbJournalPerfSpec(ITestOutputHelper output)
            : base(conf, "SQLServer", output, eventsCount: TestConstants.NumMessages)
        {
            //LinqToDB.Common.Configuration.ContinueOnCapturedContext = false;
            DotMemoryUnitTestOutput.SetOutputMethod(
                message => output.WriteLine(message));
            var connFactory = new AkkaPersistenceDataConnectionFactory(new JournalConfig(conf.GetConfig("akka.persistence.journal.linq2db")));
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
        [Fact]
        public void PersistenceActor_Must_measure_PersistGroup1000()
        {
            RunGroupBenchmark(1000,10);
        }
        
    }
}